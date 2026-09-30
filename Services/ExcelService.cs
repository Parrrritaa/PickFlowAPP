using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using PickingApp.Data;
using PickingApp.Models;

namespace PickingApp.Services
{
    // Lee y valida el Excel de pedidos con la librería ClosedXML. No guarda nada en la base de datos:
    // devuelve los pedidos válidos y PedidosController (Carga) los guarda cuando el usuario confirma.
    public class ExcelService : IExcelService
    {
        private readonly ApplicationDbContext _context;

        public ExcelService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<ExcelValidationResult> ValidarArchivoExcelAsync(Stream stream)
        {
            var result = new ExcelValidationResult();

            try
            {
                using var workbook = new XLWorkbook(stream);
                result.ArchivoLegible = true;
                var worksheet = workbook.Worksheets.FirstOrDefault();

                if (worksheet == null)
                {
                    result.IsValid = false;
                    result.Errors.Add("El archivo Excel no contiene ninguna hoja de cálculo.");
                    return result;
                }

                // La fila 1 son los encabezados; la columna A debe ser el código del pedido
                var header1 = worksheet.Cell(1, 1).GetString().Trim();

                if (!header1.Contains("Pedido", StringComparison.OrdinalIgnoreCase))
                {
                    result.IsValid = false;
                    result.Errors.Add("El archivo no cumple con la estructura esperada. La columna 1 debe ser 'CodigoPedido'. Verifica el formato e inténtalo nuevamente.");
                    return result;
                }

                var range = worksheet.RangeUsed();
                if (range == null)
                {
                    result.IsValid = false;
                    result.Errors.Add("La hoja de cálculo está vacía.");
                    return result;
                }
                result.EstructuraValida = true;

                // Se cargan una sola vez para validar todas las filas sin consultar MySQL por cada fila
                var bodegasDb = await _context.Bodegas.ToDictionaryAsync(b => b.Codigo.ToUpper(), b => b.Id);
                var ubicacionesDb = await _context.Ubicaciones.Include(u => u.Producto).ToListAsync();
                var codigosPedidoExistentes = (await _context.Pedidos.Select(p => p.CodigoPedido).ToListAsync())
                    .Select(c => c.ToUpperInvariant())
                    .ToHashSet();

                // Skip(1) salta la fila de encabezados
                var rows = range.RowsUsed().Skip(1);
                var totalFilas = 0;
                var pedidosDict = new Dictionary<string, Pedido>();

                foreach (var row in rows)
                {
                    totalFilas++;
                    var filaNum = row.RowNumber();

                    // Columnas A–I: CodigoPedido, Bodega, Pasillo, Estante, Nivel, CodigoProducto, NombreProducto, Cantidad, Prioridad
                    var codPedido = row.Cell(1).GetString().Trim();
                    var codBodega = row.Cell(2).GetString().Trim().ToUpper();
                    var pasillo = row.Cell(3).GetString().Trim().ToUpper();
                    var estante = row.Cell(4).GetString().Trim().ToUpper();
                    var nivel = row.Cell(5).GetString().Trim().ToUpper();
                    var codProducto = row.Cell(6).GetString().Trim();
                    var nomProducto = row.Cell(7).GetString().Trim();
                    var cantidadStr = row.Cell(8).GetString().Trim();
                    var prioridad = row.Cell(9).GetString().Trim();

                    if (string.IsNullOrWhiteSpace(codPedido) || string.IsNullOrWhiteSpace(codProducto))
                    {
                        continue;
                    }

                    if (!int.TryParse(cantidadStr, out int cantidad) || cantidad <= 0)
                    {
                        result.FilasConCantidadInvalida++;
                        result.Errors.Add($"Fila {filaNum}: Cantidad inválida '{cantidadStr}' para producto '{codProducto}'.");
                        continue;
                    }

                    if (!bodegasDb.TryGetValue(codBodega, out int bodegaId))
                    {
                        result.FilasConBodegaNoRegistrada++;
                        result.Errors.Add($"Fila {filaNum}: La bodega '{codBodega}' no está registrada en el sistema.");
                        continue;
                    }

                    var ubi = ubicacionesDb.FirstOrDefault(u =>
                        u.BodegaId == bodegaId &&
                        MismoValor(u.Pasillo, pasillo) &&
                        MismoValor(u.Estante, estante) &&
                        MismoValor(u.Nivel, nivel));

                    if (ubi == null)
                    {
                        result.FilasConUbicacionNoRegistrada++;
                        result.Errors.Add($"Fila {filaNum}: La ubicación en Bodega {codBodega} (Pasillo: {pasillo}, Estante: {estante}, Nivel: {nivel}) no está registrada.");
                        continue;
                    }

                    if (ubi.Producto != null && !string.Equals(ubi.Producto.Codigo, codProducto, StringComparison.OrdinalIgnoreCase))
                    {
                        result.FilasConProductoIncorrecto++;
                        result.Errors.Add($"Fila {filaNum}: El producto '{codProducto}' no corresponde a la ubicación {ubi.CodigoUbicacion} de la Bodega {codBodega} (esa ubicación está registrada para el producto '{ubi.Producto.Codigo} - {ubi.Producto.Nombre}').");
                        continue;
                    }

                    // Varias filas con el mismo código forman un solo pedido con varias líneas (Detalles)
                    if (!pedidosDict.TryGetValue(codPedido, out var pedido))
                    {
                        // Un código de pedido ya registrado no se reabre ni se renombra: dos cargas del mismo
                        // pedido duplicarían el trabajo de alistamiento para un solo pedido del cliente.
                        if (codigosPedidoExistentes.Contains(codPedido.ToUpperInvariant()))
                        {
                            result.FilasConPedidoDuplicado++;
                            result.Errors.Add($"Fila {filaNum}: El pedido '{codPedido}' ya está registrado en el sistema. Si es una corrección, edite o cancele el pedido existente en vez de volver a cargarlo.");
                            continue;
                        }

                        prioridad = string.IsNullOrWhiteSpace(prioridad) ? "Media" : prioridad;
                        if (prioridad != "Alta" && prioridad != "Media" && prioridad != "Baja")
                        {
                            prioridad = "Media";
                        }

                        pedido = new Pedido
                        {
                            CodigoPedido = codPedido,
                            BodegaId = bodegaId,
                            Prioridad = prioridad,
                            Estado = "Pendiente",
                            FechaCarga = DateTime.Now,
                            Detalles = new List<DetallePedido>()
                        };
                        pedidosDict[codPedido] = pedido;
                    }

                    pedido.Detalles.Add(new DetallePedido
                    {
                        CodigoProducto = codProducto,
                        NombreProducto = string.IsNullOrWhiteSpace(nomProducto) ? codProducto : nomProducto,
                        Cantidad = cantidad,
                        UbicacionId = ubi.Id
                    });
                }

                result.TotalFilas = totalFilas;
                result.TotalPedidos = pedidosDict.Count;
                result.PedidosValidados = pedidosDict.Values.ToList();
                // El archivo solo es válido si no hubo ningún error: se carga todo o nada
                result.IsValid = !result.Errors.Any() && result.PedidosValidados.Any();

                if (!result.PedidosValidados.Any() && !result.Errors.Any())
                {
                    result.IsValid = false;
                    result.Errors.Add("El archivo no contiene filas de datos con pedidos válidos.");
                }
            }
            catch (Exception ex)
            {
                result.IsValid = false;
                result.Errors.Add($"No fue posible leer el archivo seleccionado. Verifique que el formato sea correcto ({ex.Message}).");
            }

            return result;
        }

        // Pasillo, estante y nivel se comparan como números: "01" en el Excel es la misma ubicación que "1",
        // igual que en el código de ubicación (PAS01). Si alguno no es numérico se compara como texto.
        private static bool MismoValor(string guardado, string delExcel) =>
            int.TryParse(guardado, out var a) && int.TryParse(delExcel, out var b)
                ? a == b
                : guardado.Equals(delExcel, StringComparison.OrdinalIgnoreCase);
    }
}