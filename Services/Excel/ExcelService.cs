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
    // Un producto no registrado o que no está en la ubicación del Excel no es un error del archivo: se devuelve
    // como incidencia y el usuario decide en pantalla de dónde se recoge (ResolucionExcel).
    public class ExcelService : IExcelService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<ExcelService> _logger;

        public ExcelService(ApplicationDbContext context, ILogger<ExcelService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<ExcelValidationResult> ValidarArchivoExcelAsync(Stream stream, IReadOnlyList<ResolucionExcel> resoluciones, bool puedeEditarUbicaciones)
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

                // Se cargan una sola vez para validar todas las filas sin consultar MySQL por cada fila.
                // Van con seguimiento (tracking): si una decisión asigna un producto o activa una ubicación,
                // ese cambio se guarda junto con los pedidos en el mismo SaveChanges.
                var bodegas = await _context.Bodegas.ToListAsync();
                var bodegasDb = bodegas.ToDictionary(b => b.Codigo.ToUpper());
                var ubicacionesDb = await _context.Ubicaciones.Include(u => u.Producto).ToListAsync();
                var productosDb = (await _context.Productos.ToListAsync())
                    .ToDictionary(p => p.Codigo, StringComparer.OrdinalIgnoreCase);
                var categorias = await _context.CategoriasProducto.Select(c => c.Id).ToListAsync();
                var codigosPedidoExistentes = (await _context.Pedidos.Select(p => p.CodigoPedido).ToListAsync())
                    .Select(c => c.ToUpperInvariant())
                    .ToHashSet();

                var incidencias = new GestorIncidencias(resoluciones, bodegas, ubicacionesDb, categorias, puedeEditarUbicaciones, result);

                var rows = range.RowsUsed().Skip(1);
                var totalFilas = 0;
                // Sin distinguir mayúsculas, igual que MySQL: "PED-1" y "ped-1" son el mismo pedido
                var pedidosDict = new Dictionary<string, Pedido>(StringComparer.OrdinalIgnoreCase);

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

                    var ubi = ubicacionesDb.FirstOrDefault(u => u.BodegaId == bodega.Id && MismaPosicion(u, pasillo, estante, nivel));
                    productosDb.TryGetValue(codProducto, out var producto);
                    var fila = new FilaExcel(filaNum, codPedido, codProducto, nomProducto, bodega, pasillo, estante, nivel);

                    // Fila correcta: el producto existe y está en esa ubicación activa. Si no, la fila queda
                    // en una incidencia y se recoge de donde decidió el usuario (null mientras no decida).
                    var destino = ubi;
                    var tipo = TipoIncidencia(producto, ubi);
                    if (tipo != null)
                    {
                        if (tipo == TiposIncidenciaExcel.ProductoNoRegistrado) result.FilasConProductoNoRegistrado++;
                        else if (tipo == TiposIncidenciaExcel.ProductoIncorrecto || tipo == TiposIncidenciaExcel.UbicacionVacia) result.FilasConProductoIncorrecto++;
                        else result.FilasConUbicacionNoRegistrada++;

                        destino = incidencias.Registrar(tipo, fila, producto, ubi);
                    }

                    // El nombre del archivo debe ser el del producto registrado con ese código. Si no coincide,
                    // el usuario confirma que se cargue con el nombre registrado o corrige el código en el archivo.
                    var nombreConfirmado = true;
                    if (producto != null && !MismoNombre(nomProducto, producto.Nombre))
                    {
                        result.FilasConNombreDistinto++;
                        nombreConfirmado = incidencias.RegistrarNombre(fila, producto);
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

                        // "alta" vale como Alta; vacía o desconocida, Media
                        prioridad = Prioridades.Todas.FirstOrDefault(p => p.Equals(prioridad, StringComparison.OrdinalIgnoreCase))
                            ?? Prioridades.Media;

                        pedido = new Pedido
                        {
                            CodigoPedido = codPedido,
                            // La bodega del pedido es la de la ubicación donde se recoge (el usuario pudo elegir otra)
                            BodegaId = destino?.BodegaId ?? bodega.Id,
                            Prioridad = prioridad,
                            Estado = PedidoEstados.Pendiente,
                            FechaCarga = DateTime.Now,
                            Detalles = new List<DetallePedido>()
                        };
                        pedidosDict[codPedido] = pedido;
                    }

                    if (destino == null || !nombreConfirmado) continue;

                    // Ubicacion (navegación) enlaza también las ubicaciones nuevas, que aún no tienen Id:
                    // EF Core las inserta antes que el detalle y completa UbicacionId al guardar.
                    // Código y nombre son siempre los del producto registrado (o el que se registra en esta carga).
                    pedido.Detalles.Add(new DetallePedido
                    {
                        CodigoProducto = destino.Producto?.Codigo ?? codProducto,
                        NombreProducto = destino.Producto?.Nombre ?? (string.IsNullOrWhiteSpace(nomProducto) ? codProducto : nomProducto),
                        Cantidad = cantidad,
                        UbicacionId = destino.Id,
                        Ubicacion = destino
                    });
                }

                result.TotalFilas = totalFilas;
                result.TotalPedidos = pedidosDict.Count;
                result.PedidosValidados = pedidosDict.Values.ToList();
                result.Incidencias = incidencias.Lista;
                // El archivo solo es válido si no hubo ningún error y cada incidencia tiene una decisión válida: se carga todo o nada
                result.IsValid = !result.Errors.Any() && incidencias.TodasResueltas && result.PedidosValidados.Any();

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

        // null si la fila está bien: el producto existe y su ubicación del Excel lo guarda y está activa
        private static string? TipoIncidencia(Producto? producto, Ubicacion? ubi)
        {
            if (producto == null) return TiposIncidenciaExcel.ProductoNoRegistrado;
            if (ubi == null) return TiposIncidenciaExcel.UbicacionNoRegistrada;
            if (ubi.Producto == null) return TiposIncidenciaExcel.UbicacionVacia;
            if (ubi.Producto != producto) return TiposIncidenciaExcel.ProductoIncorrecto;
            return ubi.Activa ? null : TiposIncidenciaExcel.UbicacionInactiva;
        }

        private static bool MismaPosicion(Ubicacion u, string pasillo, string estante, string nivel) =>
            MismoValor(u.Pasillo, pasillo) && MismoValor(u.Estante, estante) && MismoValor(u.Nivel, nivel);

        // Pasillo, estante y nivel se comparan como números: "01" en el Excel es la misma ubicación que "1",
        // igual que en el código de ubicación (PAS01). Si alguno no es numérico se compara como texto.
        private static bool MismoValor(string guardado, string delExcel) =>
            int.TryParse(guardado, out var a) && int.TryParse(delExcel, out var b)
                ? a == b
                : guardado.Equals(delExcel, StringComparison.OrdinalIgnoreCase);

        // Mayúsculas, tildes y espacios repetidos no cuentan: "teclado  mecanico" es "Teclado Mecánico".
        // Una celda vacía tampoco es un nombre distinto: se usa el registrado.
        private static bool MismoNombre(string delExcel, string registrado) =>
            string.IsNullOrWhiteSpace(delExcel) ||
            string.Compare(Regex.Replace(delExcel.Trim(), @"\s+", " "), Regex.Replace(registrado.Trim(), @"\s+", " "),
                CultureInfo.InvariantCulture, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) == 0;

        private static bool EsNumero(string? valor) => valor != null && Regex.IsMatch(valor, @"^\d{1,9}$");

        // "01" → "1"; lo que no es número se deja en mayúsculas (sirve para agrupar filas en la misma incidencia)
        private static string Normalizar(string valor) => EsNumero(valor) ? int.Parse(valor).ToString() : valor.ToUpperInvariant();

        private sealed record FilaExcel(int Numero, string Pedido, string CodigoProducto, string NombreProducto,
            Bodega Bodega, string Pasillo, string Estante, string Nivel);

        // Agrupa las filas con problemas en incidencias y aplica la decisión del usuario sobre cada una.
        // Los productos y ubicaciones nuevos quedan enlazados a los pedidos, sin agregarse al contexto:
        // si el controlador no llama a SaveChanges (validación previa o archivo con errores), no se guarda nada.
        private sealed class GestorIncidencias
        {
            private readonly Dictionary<string, ResolucionExcel> _resoluciones;
            private readonly List<Bodega> _bodegas;
            private readonly List<Ubicacion> _ubicaciones;
            private readonly List<int> _categorias;
            private readonly bool _puedeEditarUbicaciones;
            private readonly ExcelValidationResult _result;

            private readonly Dictionary<string, IncidenciaExcel> _incidencias = new();
            // Clave → ubicación donde se recogen sus filas (null: falta decidir o la decisión no es válida)
            private readonly Dictionary<string, Ubicacion?> _destinos = new();
            // Clave de un nombre distinto → si el usuario confirmó cargarlo con el nombre registrado
            private readonly Dictionary<string, bool> _nombresConfirmados = new();
            private readonly List<Ubicacion> _nuevas = new();

            public GestorIncidencias(IReadOnlyList<ResolucionExcel> resoluciones, List<Bodega> bodegas, List<Ubicacion> ubicaciones,
                List<int> categorias, bool puedeEditarUbicaciones, ExcelValidationResult result)
            {
                _resoluciones = resoluciones
                    .Where(r => !string.IsNullOrEmpty(r.Clave))
                    .GroupBy(r => r.Clave!)
                    .ToDictionary(g => g.Key, g => g.First());
                _bodegas = bodegas;
                _ubicaciones = ubicaciones;
                _categorias = categorias;
                _puedeEditarUbicaciones = puedeEditarUbicaciones;
                _result = result;
            }

            public List<IncidenciaExcel> Lista => _incidencias.Values.ToList();

            public bool TodasResueltas => _destinos.Values.All(d => d != null) && _nombresConfirmados.Values.All(c => c);

            public Ubicacion? Registrar(string tipo, FilaExcel fila, Producto? producto, Ubicacion? ubi)
            {
                // Un producto no registrado agrupa todas sus filas (se registra con una sola ubicación);
                // los demás casos agrupan las filas del mismo producto en la misma ubicación del Excel
                var clave = tipo == TiposIncidenciaExcel.ProductoNoRegistrado
                    ? $"producto|{fila.CodigoProducto.ToUpperInvariant()}"
                    : $"ubicacion|{fila.CodigoProducto.ToUpperInvariant()}|{fila.Bodega.Id}|{Normalizar(fila.Pasillo)}|{Normalizar(fila.Estante)}|{Normalizar(fila.Nivel)}";

                if (!_incidencias.TryGetValue(clave, out var incidencia))
                {
                    incidencia = Crear(clave, tipo, fila, producto, ubi);
                    _incidencias[clave] = incidencia;
                    _destinos[clave] = Resolver(incidencia, producto, ubi);
                }

                AgregarFila(incidencia, fila);
                return _destinos[clave];
            }

            // Nombre distinto al registrado: no cambia de dónde se recoge la fila, solo pide confirmar
            // que se carga con el nombre registrado. Agrupa las filas del mismo producto con el mismo nombre.
            public bool RegistrarNombre(FilaExcel fila, Producto producto)
            {
                var clave = $"nombre|{producto.Codigo.ToUpperInvariant()}|{fila.NombreProducto.Trim().ToUpperInvariant()}";

                if (!_incidencias.TryGetValue(clave, out var incidencia))
                {
                    incidencia = new IncidenciaExcel
                    {
                        Clave = clave,
                        Tipo = TiposIncidenciaExcel.NombreDistinto,
                        Titulo = $"El nombre no coincide con {producto.Codigo}",
                        Detalle = $"El archivo dice «{fila.NombreProducto}», pero {producto.Codigo} está registrado como «{producto.Nombre}». " +
                                  "Si el código del producto es el equivocado, corríjalo y vuelva a validar.",
                        CodigoProducto = producto.Codigo,
                        NombreProducto = producto.Nombre,
                        BodegaId = fila.Bodega.Id
                    };
                    _incidencias[clave] = incidencia;
                    _nombresConfirmados[clave] = ConfirmarNombre(incidencia);
                }

                AgregarFila(incidencia, fila);
                return _nombresConfirmados[clave];
            }

            private bool ConfirmarNombre(IncidenciaExcel incidencia)
            {
                if (!_resoluciones.TryGetValue(incidencia.Clave, out var r) || string.IsNullOrWhiteSpace(r.Opcion))
                {
                    if (_resoluciones.Count > 0) Error(incidencia, "Confirme con qué nombre se cargan estas filas.");
                    return false;
                }
                if (r.Opcion == ResolucionExcel.OpcionNombreRegistrado) return true;

                Error(incidencia, "Esa opción no está disponible. Elija otra.");
                return false;
            }

            private static void AgregarFila(IncidenciaExcel incidencia, FilaExcel fila)
            {
                incidencia.Filas.Add(fila.Numero);
                if (!incidencia.Pedidos.Contains(fila.Pedido)) incidencia.Pedidos.Add(fila.Pedido);
            }

            private IncidenciaExcel Crear(string clave, string tipo, FilaExcel fila, Producto? producto, Ubicacion? ubi)
            {
                var numerica = EsNumero(fila.Pasillo) && EsNumero(fila.Estante) && EsNumero(fila.Nivel);
                var lugar = (ubi != null ? ubi.CodigoUbicacion
                    : numerica ? Ubicacion.GenerarCodigo(fila.Pasillo, fila.Estante, fila.Nivel)
                    : $"Pasillo {fila.Pasillo}, estante {fila.Estante}, nivel {fila.Nivel}") + $" de {fila.Bodega.Codigo}";
                var codigo = producto?.Codigo ?? fila.CodigoProducto;
                var descripcion = producto != null ? $"{producto.Codigo} · {producto.Nombre}" : codigo;

                var incidencia = new IncidenciaExcel
                {
                    Clave = clave,
                    Tipo = tipo,
                    CodigoProducto = codigo,
                    NombreProducto = producto?.Nombre ?? (string.IsNullOrWhiteSpace(fila.NombreProducto) ? codigo : fila.NombreProducto),
                    BodegaId = fila.Bodega.Id,
                    Pasillo = numerica ? Normalizar(fila.Pasillo) : string.Empty,
                    Estante = numerica ? Normalizar(fila.Estante) : string.Empty,
                    Nivel = numerica ? Normalizar(fila.Nivel) : string.Empty,
                    UbicacionExiste = ubi != null
                };

                switch (tipo)
                {
                    case TiposIncidenciaExcel.ProductoNoRegistrado:
                        incidencia.Titulo = $"El producto {codigo} no está registrado";
                        var estado = ubi == null ? "no está registrada" : ubi.Producto == null ? "está vacía" : $"guarda {ubi.Producto.Codigo}";
                        incidencia.Detalle = $"Regístrelo para cargar sus filas. El archivo lo pide en {lugar}, que {estado}.";
                        break;
                    case TiposIncidenciaExcel.UbicacionNoRegistrada:
                        incidencia.Titulo = $"{lugar} no está registrada";
                        incidencia.Detalle = $"El archivo pide {descripcion} de ahí. Elija de dónde se recoge.";
                        break;
                    case TiposIncidenciaExcel.UbicacionVacia:
                        incidencia.Titulo = $"{lugar} está vacía";
                        incidencia.Detalle = $"El archivo pide {descripcion} de ahí, pero la ubicación no tiene producto asignado.";
                        break;
                    case TiposIncidenciaExcel.ProductoIncorrecto:
                        incidencia.Titulo = $"{codigo} no está en {lugar}";
                        incidencia.Detalle = $"Esa ubicación guarda {ubi!.Producto!.Codigo} · {ubi.Producto.Nombre}. Elija de dónde se recoge {descripcion}.";
                        break;
                    default:
                        incidencia.Titulo = $"{lugar} está inactiva";
                        incidencia.Detalle = $"Guarda {descripcion}, pero la ubicación está desactivada.";
                        break;
                }

                if (producto != null)
                {
                    incidencia.Ubicaciones = _ubicaciones
                        .Where(u => u.Producto == producto && u.Activa && _bodegas.Any(b => b.Id == u.BodegaId && b.Activa))
                        .OrderBy(u => u.BodegaId != fila.Bodega.Id)
                        .ThenBy(u => u.CodigoUbicacion)
                        .Select(u => new OpcionUbicacionExcel
                        {
                            Id = u.Id,
                            Codigo = u.CodigoUbicacion,
                            BodegaId = u.BodegaId,
                            Bodega = _bodegas.First(b => b.Id == u.BodegaId).Nombre
                        })
                        .ToList();

                    if (incidencia.Ubicaciones.Count == 0) incidencia.Detalle += $" {codigo} no tiene ubicaciones activas.";
                }

                // La ubicación del Excel se puede dejar lista si está vacía, o si guarda este producto pero está inactiva
                var vacia = ubi != null && ubi.Producto == null;
                var inactiva = ubi != null && !ubi.Activa && (vacia || ubi.Producto == producto);
                if (vacia || inactiva)
                {
                    if (_puedeEditarUbicaciones)
                    {
                        incidencia.TextoCorregir = vacia && inactiva ? $"Guardar {codigo} en {ubi!.CodigoUbicacion} y activarla"
                            : vacia ? $"Guardar {codigo} en {ubi!.CodigoUbicacion}"
                            : $"Activar {ubi!.CodigoUbicacion} y recoger de ahí";
                    }
                    else
                    {
                        incidencia.Detalle += " Asignar o activar esa ubicación lo hace el Administrador.";
                    }
                }

                return incidencia;
            }

            private Ubicacion? Resolver(IncidenciaExcel incidencia, Producto? producto, Ubicacion? ubi)
            {
                // Sin decisiones (primera validación) la incidencia solo se informa
                if (!_resoluciones.TryGetValue(incidencia.Clave, out var r) || string.IsNullOrWhiteSpace(r.Opcion))
                {
                    if (_resoluciones.Count > 0) Error(incidencia, "Elija qué hacer con estas filas.");
                    return null;
                }

                var productoNuevo = producto == null;
                producto ??= ProductoNuevo(incidencia, r);
                if (producto == null) return null;

                var posicion = _result.Cambios.Count;
                Ubicacion? destino;
                if (r.Opcion == ResolucionExcel.OpcionNueva)
                {
                    destino = UbicacionNueva(incidencia, producto, r);
                }
                else if (r.Opcion == ResolucionExcel.OpcionCorregir)
                {
                    destino = Corregir(incidencia, producto, ubi);
                }
                else
                {
                    // Solo se acepta una de las ubicaciones ofrecidas para este producto
                    destino = int.TryParse(r.Opcion, out var id) && incidencia.Ubicaciones.Any(o => o.Id == id)
                        ? _ubicaciones.First(u => u.Id == id)
                        : null;
                    if (destino == null) Error(incidencia, "Esa ubicación ya no está disponible para el producto. Vuelva a validar el archivo.");
                }

                if (destino != null && productoNuevo)
                {
                    _result.Cambios.Insert(posicion, $"Producto {producto.Codigo} · {producto.Nombre} registrado");
                }
                return destino;
            }

            private Producto? ProductoNuevo(IncidenciaExcel incidencia, ResolucionExcel r)
            {
                var nombre = (r.NombreProducto ?? string.Empty).Trim();

                if (incidencia.CodigoProducto.Length > 50)
                {
                    Error(incidencia, "El código del producto admite hasta 50 caracteres. Corríjalo en el archivo.");
                    return null;
                }
                if (nombre.Length == 0 || nombre.Length > 150)
                {
                    Error(incidencia, "El nombre del producto es obligatorio y admite hasta 150 caracteres.");
                    return null;
                }
                if (!int.TryParse(r.UnidadesPorCaja, out var unidades) || unidades < 1 || unidades > Producto.MaxUnidadesPorCaja)
                {
                    Error(incidencia, $"La cantidad máxima por caja debe ser un número entero entre 1 y {Producto.MaxUnidadesPorCaja}.");
                    return null;
                }

                return new Producto
                {
                    Codigo = incidencia.CodigoProducto,
                    Nombre = nombre,
                    // Sin categoría válida queda sin clasificar (usa la predeterminada)
                    CategoriaProductoId = int.TryParse(r.CategoriaProductoId, out var categoriaId) && _categorias.Contains(categoriaId) ? categoriaId : null,
                    UnidadesPorCaja = unidades
                };
            }

            private Ubicacion? UbicacionNueva(IncidenciaExcel incidencia, Producto producto, ResolucionExcel r)
            {
                var bodega = _bodegas.FirstOrDefault(b => b.Id == r.BodegaId && b.Activa);
                if (bodega == null)
                {
                    Error(incidencia, "Elija una bodega activa para la ubicación nueva.");
                    return null;
                }
                if (!EsNumero(r.Pasillo) || !EsNumero(r.Estante) || !EsNumero(r.Nivel))
                {
                    Error(incidencia, "Pasillo, estante y nivel son obligatorios y solo deben contener números.");
                    return null;
                }

                var pasillo = Normalizar(r.Pasillo!);
                var estante = Normalizar(r.Estante!);
                var nivel = Normalizar(r.Nivel!);
                var codigo = Ubicacion.GenerarCodigo(pasillo, estante, nivel);

                if (_ubicaciones.Any(u => u.BodegaId == bodega.Id && (u.CodigoUbicacion == codigo || MismaPosicion(u, pasillo, estante, nivel))))
                {
                    Error(incidencia, $"La ubicación {codigo} ya existe en {bodega.Nombre}. Elija una posición libre.");
                    return null;
                }

                // Dos decisiones de esta carga pueden pedir la misma ubicación nueva: sirve si es para el mismo producto
                var planeada = _nuevas.FirstOrDefault(u => u.BodegaId == bodega.Id && u.CodigoUbicacion == codigo);
                if (planeada != null)
                {
                    if (planeada.Producto == producto) return planeada;
                    Error(incidencia, $"Otra decisión de esta carga ya crea {codigo} en {bodega.Nombre} para {planeada.Producto!.Codigo}. Elija otra posición.");
                    return null;
                }

                var nueva = new Ubicacion
                {
                    BodegaId = bodega.Id,
                    CodigoUbicacion = codigo,
                    Pasillo = pasillo,
                    Estante = estante,
                    Nivel = nivel,
                    Activa = true,
                    Producto = producto
                };
                _nuevas.Add(nueva);
                _result.Cambios.Add($"Ubicación {codigo} creada en {bodega.Codigo} para {producto.Codigo}");
                return nueva;
            }

            // Deja lista la ubicación del Excel: le asigna el producto si está vacía y la activa si está inactiva
            private Ubicacion? Corregir(IncidenciaExcel incidencia, Producto producto, Ubicacion? ubi)
            {
                if (!_puedeEditarUbicaciones || ubi == null)
                {
                    Error(incidencia, "Esa opción no está disponible. Elija otra.");
                    return null;
                }
                if (ubi.Producto != null && ubi.Producto != producto)
                {
                    Error(incidencia, $"{ubi.CodigoUbicacion} ya guarda {ubi.Producto.Codigo} (otra decisión de esta carga). Elija otra opción.");
                    return null;
                }

                var bodega = _bodegas.First(b => b.Id == ubi.BodegaId).Codigo;
                if (ubi.Producto == null)
                {
                    ubi.Producto = producto;
                    _result.Cambios.Add($"Producto {producto.Codigo} asignado a la ubicación {ubi.CodigoUbicacion} de {bodega}");
                }
                if (!ubi.Activa)
                {
                    ubi.Activa = true;
                    _result.Cambios.Add($"Ubicación {ubi.CodigoUbicacion} de {bodega} activada");
                }
                return ubi;
            }

            private void Error(IncidenciaExcel incidencia, string mensaje) => _result.ErroresResolucion[incidencia.Clave] = mensaje;
        }
    }
}
