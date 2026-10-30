using System.Globalization;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using PickingApp.Data;
using PickingApp.Models;

namespace PickingApp.Services
{
    // Lee y valida el Excel de pedidos con la librería ClosedXML. No guarda nada en la base de datos:
    // devuelve los pedidos válidos y PedidosController (Carga) los guarda cuando el usuario confirma.
    // El cliente le pide cada pedido a una bodega; la ubicación de cada producto la elige el sistema. Un producto
    // que esa bodega no tiene no es un error del archivo: la línea no entra y el pedido queda con una novedad.
    public class ExcelService : IExcelService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<ExcelService> _logger;

        public ExcelService(ApplicationDbContext context, ILogger<ExcelService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<ExcelValidationResult> ValidarArchivoExcelAsync(Stream stream, IReadOnlyList<ResolucionExcel> resoluciones, int? bodegaDelUsuario)
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

                // El formato anterior traía Pasillo, Estante y Nivel en las columnas C a E
                if (worksheet.Cell(1, 3).GetString().Contains("Pasillo", StringComparison.OrdinalIgnoreCase))
                {
                    result.IsValid = false;
                    result.Errors.Add("El archivo usa el formato anterior, con Pasillo, Estante y Nivel. Ahora la ubicación la elige el sistema: " +
                                      "deje solo las columnas " + ColumnasEsperadas + ".");
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
                var bodegas = await _context.Bodegas.AsNoTracking().ToListAsync();
                var bodegasDb = bodegas.ToDictionary(b => b.Codigo.ToUpper());
                var productosDb = (await _context.Productos.AsNoTracking().ToListAsync())
                    .ToDictionary(p => p.Codigo, StringComparer.OrdinalIgnoreCase);

                // No se lleva inventario: un producto está disponible en una bodega activa si tiene allí una ubicación
                // activa. Se recoge de la de menor código.
                var disponibles = (await _context.Ubicaciones.AsNoTracking()
                        .Where(u => u.Activa && u.ProductoId != null && u.Bodega!.Activa)
                        .Select(u => new { u.Id, ProductoId = u.ProductoId!.Value, u.BodegaId, u.CodigoUbicacion })
                        .ToListAsync())
                    .GroupBy(u => (u.ProductoId, u.BodegaId))
                    .ToDictionary(g => g.Key, g => g.OrderBy(u => u.CodigoUbicacion, StringComparer.Ordinal).First().Id);
                var bodegasConProducto = disponibles.Keys
                    .GroupBy(k => k.ProductoId)
                    .ToDictionary(g => g.Key, g => g.Select(k => bodegas.First(b => b.Id == k.BodegaId).Nombre).OrderBy(n => n).ToList());

                // El código de pedido es único por bodega
                var codigosPedidoExistentes = (await _context.Pedidos.Select(p => new { p.BodegaId, p.CodigoPedido }).ToListAsync())
                    .Select(p => (p.BodegaId, p.CodigoPedido.ToUpperInvariant()))
                    .ToHashSet();

                var nombres = new ConfirmacionNombres(resoluciones, result);

                var rows = range.RowsUsed().Skip(1);
                var totalFilas = 0;
                // "PED-1" de dos bodegas son dos pedidos; sin distinguir mayúsculas, igual que MySQL
                var pedidosDict = new Dictionary<(int BodegaId, string Codigo), Pedido>();
                // Productos de cada pedido y los avisos de los que su bodega no tiene
                var productosPorPedido = new Dictionary<Pedido, HashSet<int>>();
                var avisosPorPedido = new Dictionary<Pedido, List<string>>();
                var conDisponible = new HashSet<Pedido>();

                foreach (var row in rows)
                {
                    totalFilas++;
                    var filaNum = row.RowNumber();

                    // Columnas A–F: CodigoPedido, Bodega, CodigoProducto, NombreProducto, Cantidad, Prioridad
                    var codPedido = row.Cell(1).GetString().Trim();
                    var codBodega = row.Cell(2).GetString().Trim().ToUpper();
                    var codProducto = row.Cell(3).GetString().Trim();
                    var nomProducto = row.Cell(4).GetString().Trim();
                    var cantidadStr = row.Cell(5).GetString().Trim();
                    var prioridad = row.Cell(6).GetString().Trim();

                    if (string.IsNullOrWhiteSpace(codPedido) || string.IsNullOrWhiteSpace(codProducto))
                    {
                        continue;
                    }

                    // Largos de las columnas en MySQL (Pedido.CodigoPedido, Producto.Codigo y Producto.Nombre)
                    if (codPedido.Length > 50 || codProducto.Length > 50 || nomProducto.Length > 150)
                    {
                        result.Errors.Add($"Fila {filaNum}: el código del pedido y el del producto admiten hasta 50 caracteres, y el nombre del producto hasta 150.");
                        continue;
                    }

                    if (!int.TryParse(cantidadStr, out int cantidad) || cantidad <= 0)
                    {
                        result.FilasConCantidadInvalida++;
                        result.Errors.Add($"Fila {filaNum}: Cantidad inválida '{cantidadStr}' para producto '{codProducto}'.");
                        continue;
                    }

                    if (!bodegasDb.TryGetValue(codBodega, out var bodega))
                    {
                        result.FilasConBodegaNoRegistrada++;
                        result.Errors.Add($"Fila {filaNum}: La bodega '{codBodega}' no está registrada en el sistema.");
                        continue;
                    }

                    if (bodegaDelUsuario != null && bodega.Id != bodegaDelUsuario)
                    {
                        result.FilasDeOtraBodega++;
                        result.Errors.Add($"Fila {filaNum}: el pedido '{codPedido}' es para {bodega.Nombre}. Solo puede cargar pedidos de su bodega.");
                        continue;
                    }

                    if (!productosDb.TryGetValue(codProducto, out var producto))
                    {
                        result.FilasConProductoNoRegistrado++;
                        result.Errors.Add($"Fila {filaNum}: el producto '{codProducto}' no está registrado. Revise el código o regístrelo en Productos antes de cargar el pedido.");
                        continue;
                    }

                    // Varias filas con el mismo código y bodega forman un solo pedido con varias líneas (Detalles)
                    var clave = (bodega.Id, codPedido.ToUpperInvariant());
                    if (!pedidosDict.TryGetValue(clave, out var pedido))
                    {
                        // Un código de pedido ya registrado no se reabre ni se renombra: dos cargas del mismo
                        // pedido duplicarían el trabajo de alistamiento para un solo pedido del cliente.
                        if (codigosPedidoExistentes.Contains(clave))
                        {
                            result.FilasConPedidoDuplicado++;
                            result.Errors.Add($"Fila {filaNum}: El pedido '{codPedido}' ya está registrado en {bodega.Nombre}. Si es una corrección, edite o cancele el pedido existente en vez de volver a cargarlo.");
                            continue;
                        }

                        // "alta" vale como Alta; vacía o desconocida, Media
                        prioridad = Prioridades.Todas.FirstOrDefault(p => p.Equals(prioridad, StringComparison.OrdinalIgnoreCase))
                            ?? Prioridades.Media;

                        pedido = new Pedido
                        {
                            CodigoPedido = codPedido,
                            BodegaId = bodega.Id,
                            Prioridad = prioridad,
                            Estado = PedidoEstados.Pendiente,
                            FechaCarga = DateTime.Now,
                            Detalles = new List<DetallePedido>()
                        };
                        pedidosDict[clave] = pedido;
                        productosPorPedido[pedido] = new HashSet<int>();
                        avisosPorPedido[pedido] = new List<string>();
                    }
                    productosPorPedido[pedido].Add(producto.Id);

                    if (!disponibles.TryGetValue((producto.Id, bodega.Id), out var ubicacionId))
                    {
                        result.FilasNoDisponibles++;
                        var otras = bodegasConProducto.GetValueOrDefault(producto.Id);
                        var descripcion = $"{producto.Codigo} · {producto.Nombre} ({cantidad} u.) no está disponible en {bodega.Nombre}. " +
                                          (otras == null ? "Ninguna bodega lo maneja." : $"Disponible en: {string.Join(", ", otras)}.");
                        // RegistradoPor lo completa PedidosController con quien confirma la carga
                        pedido.Novedades.Add(new NovedadPedido
                        {
                            TipoNovedad = TiposNovedad.NoDisponible,
                            Descripcion = descripcion.Length > 500 ? descripcion[..500] : descripcion,
                            FechaRegistro = DateTime.Now
                        });
                        avisosPorPedido[pedido].Add($"Fila {filaNum}, pedido {codPedido}: {descripcion}");
                        continue;
                    }
                    conDisponible.Add(pedido);

                    // El nombre del archivo debe ser el del producto registrado con ese código. Si no coincide,
                    // el usuario confirma que se cargue con el nombre registrado o corrige el código en el archivo.
                    if (!MismoNombre(nomProducto, producto.Nombre))
                    {
                        result.FilasConNombreDistinto++;
                        if (!nombres.Registrar(filaNum, codPedido, nomProducto, producto)) continue;
                    }

                    pedido.Detalles.Add(new DetallePedido
                    {
                        CodigoProducto = producto.Codigo,
                        NombreProducto = producto.Nombre,
                        Cantidad = cantidad,
                        UbicacionId = ubicacionId
                    });
                }

                // Sin ningún producto disponible en su bodega el pedido no se carga: se recomiendan las que sí los tienen
                foreach (var (pedido, productos) in productosPorPedido.Where(p => !conDisponible.Contains(p.Key)))
                {
                    var sugeridas = bodegas
                        .Where(b => b.Activa && b.Id != pedido.BodegaId)
                        .Select(b => (b.Nombre, Tiene: productos.Count(p => disponibles.ContainsKey((p, b.Id)))))
                        .Where(x => x.Tiene > 0)
                        .OrderByDescending(x => x.Tiene).ThenBy(x => x.Nombre)
                        .Select(x => $"{x.Nombre} ({x.Tiene} de {productos.Count})")
                        .ToList();
                    result.Errors.Add($"El pedido '{pedido.CodigoPedido}' no se puede cargar en {bodegas.First(b => b.Id == pedido.BodegaId).Nombre}: " +
                                      "ninguno de sus productos está disponible allí. " +
                                      (sugeridas.Any() ? $"Bodegas que tienen sus productos: {string.Join(", ", sugeridas)}." : "Ninguna bodega maneja sus productos."));
                    pedidosDict.Remove((pedido.BodegaId, pedido.CodigoPedido.ToUpperInvariant()));
                }

                result.TotalFilas = totalFilas;
                result.TotalPedidos = pedidosDict.Count;
                result.PedidosValidados = pedidosDict.Values.ToList();
                result.NoDisponibles = result.PedidosValidados.SelectMany(p => avisosPorPedido[p]).ToList();
                result.Incidencias = nombres.Lista;
                // El archivo solo es válido si no hubo ningún error y cada nombre distinto está confirmado: se carga todo o nada
                result.IsValid = !result.Errors.Any() && nombres.TodasConfirmadas && result.PedidosValidados.Any();

                if (!result.PedidosValidados.Any() && !result.Errors.Any())
                {
                    result.IsValid = false;
                    result.Errors.Add("El archivo no contiene filas de datos con pedidos válidos.");
                }
            }
            catch (Exception ex)
            {
                result.IsValid = false;
                // El detalle técnico queda en el registro del servidor; al usuario le sirve saber qué revisar
                _logger.LogWarning(ex, "No se pudo leer el archivo Excel de pedidos.");
                result.Errors.Add("No fue posible leer el archivo seleccionado. Verifique que sea un Excel (.xlsx) válido, que no esté dañado y que no tenga contraseña.");
            }

            return result;
        }

        public const string ColumnasEsperadas = "CodigoPedido, Bodega, CodigoProducto, NombreProducto, Cantidad y Prioridad";

        // Mayúsculas, tildes y espacios repetidos no cuentan: "teclado  mecanico" es "Teclado Mecánico".
        // Una celda vacía tampoco es un nombre distinto: se usa el registrado.
        private static bool MismoNombre(string delExcel, string registrado) =>
            string.IsNullOrWhiteSpace(delExcel) ||
            string.Compare(Regex.Replace(delExcel.Trim(), @"\s+", " "), Regex.Replace(registrado.Trim(), @"\s+", " "),
                CultureInfo.InvariantCulture, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) == 0;

        // Agrupa las filas con un nombre distinto al registrado y aplica la confirmación del usuario sobre cada grupo
        private sealed class ConfirmacionNombres
        {
            private readonly Dictionary<string, ResolucionExcel> _resoluciones;
            private readonly ExcelValidationResult _result;
            private readonly Dictionary<string, IncidenciaExcel> _incidencias = new();
            // Clave → si el usuario confirmó cargar esas filas con el nombre registrado
            private readonly Dictionary<string, bool> _confirmados = new();

            public ConfirmacionNombres(IReadOnlyList<ResolucionExcel> resoluciones, ExcelValidationResult result)
            {
                _resoluciones = resoluciones
                    .Where(r => !string.IsNullOrEmpty(r.Clave))
                    .GroupBy(r => r.Clave!)
                    .ToDictionary(g => g.Key, g => g.First());
                _result = result;
            }

            public List<IncidenciaExcel> Lista => _incidencias.Values.ToList();

            public bool TodasConfirmadas => _confirmados.Values.All(c => c);

            // Agrupa las filas del mismo producto con el mismo nombre. True si ya están confirmadas.
            public bool Registrar(int fila, string pedido, string nombreDelArchivo, Producto producto)
            {
                var clave = $"nombre|{producto.Codigo.ToUpperInvariant()}|{nombreDelArchivo.Trim().ToUpperInvariant()}";

                if (!_incidencias.TryGetValue(clave, out var incidencia))
                {
                    incidencia = new IncidenciaExcel
                    {
                        Clave = clave,
                        Tipo = TiposIncidenciaExcel.NombreDistinto,
                        Titulo = $"El nombre no coincide con {producto.Codigo}",
                        Detalle = $"El archivo dice «{nombreDelArchivo}», pero {producto.Codigo} está registrado como «{producto.Nombre}». " +
                                  "Si el código del producto es el equivocado, corríjalo y vuelva a validar.",
                        CodigoProducto = producto.Codigo,
                        NombreProducto = producto.Nombre
                    };
                    _incidencias[clave] = incidencia;
                    _confirmados[clave] = Confirmar(incidencia);
                }

                incidencia.Filas.Add(fila);
                if (!incidencia.Pedidos.Contains(pedido)) incidencia.Pedidos.Add(pedido);
                return _confirmados[clave];
            }

            private bool Confirmar(IncidenciaExcel incidencia)
            {
                // Sin decisiones (primera validación) la incidencia solo se informa
                if (!_resoluciones.TryGetValue(incidencia.Clave, out var r) || string.IsNullOrWhiteSpace(r.Opcion))
                {
                    if (_resoluciones.Count > 0) _result.ErroresResolucion[incidencia.Clave] = "Confirme con qué nombre se cargan estas filas.";
                    return false;
                }
                if (r.Opcion == ResolucionExcel.OpcionNombreRegistrado) return true;

                _result.ErroresResolucion[incidencia.Clave] = "Esa opción no está disponible. Elija otra.";
                return false;
            }
        }
    }
}
