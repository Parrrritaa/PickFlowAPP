using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickingApp.Data;
using PickingApp.Models;
using PickingApp.Services;

namespace PickingApp.Controllers
{
    [Authorize]
    public class PedidosController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IExcelService _excelService;
        private readonly IPickingEngine _pickingEngine;
        private readonly IAuditService _auditService;

        public PedidosController(
            ApplicationDbContext context,
            IExcelService excelService,
            IPickingEngine pickingEngine,
            IAuditService auditService)
        {
            _context = context;
            _excelService = excelService;
            _pickingEngine = pickingEngine;
            _auditService = auditService;
        }

        // CU-24: Consultar pedidos con filtros
        // vista = "divididos" muestra aparte los pedidos divididos con sus partes, para no saturar la lista principal
        public async Task<IActionResult> Index(string? estado, string? prioridad, int? bodegaId, string? busqueda,
            int? empleadoId, DateTime? fechaDesde, DateTime? fechaHasta, string? vista)
        {
            var verDivididos = vista == "divididos";

            var query = _context.Pedidos
                .Include(p => p.Bodega)
                .Include(p => p.Empleado)
                .Include(p => p.Partes)
                    .ThenInclude(pa => pa.Empleado)
                .Include(p => p.Partes)
                    .ThenInclude(pa => pa.Detalles)
                .AsSplitQuery()
                .AsQueryable();

            query = verDivididos
                ? query.Where(p => p.Partes.Any())
                : query.Where(p => p.PedidoPadreId == null && !p.Partes.Any());

            if (!string.IsNullOrEmpty(estado))
            {
                query = query.Where(p => p.Estado == estado);
            }

            if (!string.IsNullOrEmpty(prioridad))
            {
                query = query.Where(p => p.Prioridad == prioridad);
            }

            if (bodegaId.HasValue && bodegaId.Value > 0)
            {
                query = query.Where(p => p.BodegaId == bodegaId.Value);
            }

            if (!string.IsNullOrWhiteSpace(busqueda))
            {
                var b = busqueda.Trim().ToLower();
                query = query.Where(p => p.CodigoPedido.ToLower().Contains(b) ||
                                         p.Detalles.Any(d => d.NombreProducto.ToLower().Contains(b) || d.CodigoProducto.ToLower().Contains(b)));
            }

            // empleadoId = -1 filtra los pedidos sin empleado asignado; en los divididos se mira el empleado de sus partes
            if (empleadoId == -1)
            {
                query = verDivididos
                    ? query.Where(p => p.Partes.Any(pa => pa.EmpleadoId == null && !PedidoEstados.Finalizados.Contains(pa.Estado)))
                    : query.Where(p => p.EmpleadoId == null);
            }
            else if (empleadoId.HasValue && empleadoId.Value > 0)
            {
                query = verDivididos
                    ? query.Where(p => p.Partes.Any(pa => pa.EmpleadoId == empleadoId.Value))
                    : query.Where(p => p.EmpleadoId == empleadoId.Value);
            }

            if (fechaDesde.HasValue)
            {
                var desde = fechaDesde.Value.Date;
                query = query.Where(p => p.FechaCarga >= desde);
            }

            if (fechaHasta.HasValue)
            {
                var hastaExclusivo = fechaHasta.Value.Date.AddDays(1);
                query = query.Where(p => p.FechaCarga < hastaExclusivo);
            }

            // Se listan todas las bodegas (no solo las activas) para poder consultar pedidos históricos de bodegas desactivadas
            ViewBag.Bodegas = await _context.Bodegas.OrderBy(b => b.Codigo).ToListAsync();
            ViewBag.Empleados = await _context.Empleados.OrderBy(e => e.NombreCompleto).ToListAsync();
            ViewBag.FiltroEstado = estado;
            ViewBag.FiltroPrioridad = prioridad;
            ViewBag.FiltroBodegaId = bodegaId;
            ViewBag.FiltroEmpleadoId = empleadoId;
            ViewBag.FiltroFechaDesde = fechaDesde?.ToString("yyyy-MM-dd");
            ViewBag.FiltroFechaHasta = fechaHasta?.ToString("yyyy-MM-dd");
            ViewBag.Busqueda = busqueda;
            ViewBag.Vista = verDivididos ? "divididos" : null;
            ViewBag.DivididosEnCurso = await _context.Pedidos.CountAsync(p => p.Estado == PedidoEstados.Dividido);

            var pedidos = await query
                .OrderByDescending(p => p.Prioridad == "Alta")
                .ThenByDescending(p => p.FechaCarga)
                .ThenBy(p => p.CodigoPedido)
                .ToListAsync();

            return View(pedidos);
        }

        // Detalle de Pedido (con ruta de picking de ubicaciones, productos, novedades)
        public async Task<IActionResult> Detalle(int id)
        {
            var pedido = await _context.Pedidos
                .Include(p => p.Bodega)
                .Include(p => p.Empleado)
                .Include(p => p.Detalles)
                    .ThenInclude(d => d.Ubicacion)
                        .ThenInclude(u => u!.Producto)
                            .ThenInclude(pr => pr!.Categoria)
                .Include(p => p.Novedades)
                .Include(p => p.PedidoPadre)
                    .ThenInclude(pp => pp!.Partes)
                .Include(p => p.Partes)
                    .ThenInclude(pa => pa.Empleado)
                .Include(p => p.Partes)
                    .ThenInclude(pa => pa.Detalles)
                .Include(p => p.Partes)
                    .ThenInclude(pa => pa.Novedades)
                .AsSplitQuery()
                .FirstOrDefaultAsync(p => p.Id == id);

            if (pedido == null) return NotFound();

            // Tiempo estándar según las categorías de los productos, comparado con una jornada completa
            ViewBag.TiempoEstandar = pedido.TiempoEstandarMinutos ?? await _pickingEngine.CalcularTiempoEstandarAsync(pedido.Id);
            ViewBag.CategoriaPredeterminada = await _context.CategoriasProducto
                .Where(c => c.EsPredeterminada).Select(c => c.Nombre).FirstOrDefaultAsync();
            ViewBag.MaxHorasJornada = await ObtenerMaxHorasJornadaAsync();
            ViewBag.TiemposPartes = await _pickingEngine.CalcularTiemposEstandarAsync(pedido.Partes.Select(pa => pa.Id).ToList());

            // Cargar empleados disponibles para posible asignación
            ViewBag.EmpleadosDisponibles = await _context.Empleados
                .Where(e => e.Activo)
                .OrderBy(e => e.NombreCompleto)
                .ToListAsync();

            return View(pedido);
        }

        // CU-23: Cargar archivo Excel de pedidos
        [Authorize(Roles = "Administrador,Supervisor")]
        [HttpGet]
        public IActionResult CargarExcel()
        {
            return View();
        }

        [Authorize(Roles = "Administrador,Supervisor")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CargarExcel(IFormFile archivoExcel)
        {
            if (archivoExcel == null || archivoExcel.Length == 0)
            {
                TempData["ErrorMessage"] = "No fue posible leer el archivo seleccionado. Verifica que el formato sea correcto e inténtalo nuevamente.";
                return View();
            }

            if (!archivoExcel.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            {
                TempData["ErrorMessage"] = "El archivo no cumple con la estructura esperada. Debe ser un archivo de Excel (.xlsx). Verifica el formato e inténtalo nuevamente.";
                return View();
            }

            using var stream = archivoExcel.OpenReadStream();
            var validacion = await _excelService.ValidarArchivoExcelAsync(stream);

            if (!validacion.IsValid)
            {
                ViewBag.Errores = validacion.Errors;
                TempData["ErrorMessage"] = "El archivo contiene errores de validación. Revise la lista de observaciones y corríjalas antes de reintentar.";
                return View();
            }

            // Registrar pedidos válidos en base de datos (Paso 8 del CU-23)
            var userEmail = User.FindFirstValue(ClaimTypes.Email) ?? "Usuario";
            var userRole = User.FindFirstValue(ClaimTypes.Role) ?? "Usuario";

            // Los códigos de pedido duplicados ya se rechazaron en ValidarArchivoExcelAsync: un código
            // repetido no se renombra ni se reabre, porque duplicaría el trabajo de alistamiento.
            _context.Pedidos.AddRange(validacion.PedidosValidados);

            await _context.SaveChangesAsync();

            await _auditService.LogAsync(userEmail, userRole, "Carga Excel Pedidos", "Pedidos", 
                $"Se procesó el archivo '{archivoExcel.FileName}' registrando {validacion.TotalPedidos} pedidos ({validacion.TotalFilas} líneas de productos).");

            TempData["SuccessMessage"] = $"El archivo fue procesado correctamente y los {validacion.TotalPedidos} pedidos quedaron registrados en el sistema.";
            return RedirectToAction(nameof(Index));
        }

        // CU-23: validar el archivo en el servidor sin registrar nada (resultado de validación antes de confirmar la carga)
        [Authorize(Roles = "Administrador,Supervisor")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ValidarExcel(IFormFile archivoExcel)
        {
            var esXlsx = archivoExcel != null && archivoExcel.Length > 0
                && archivoExcel.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase);
            if (!esXlsx)
            {
                return Json(new
                {
                    valido = false,
                    tipoValido = false,
                    errores = new[] { "El archivo debe ser un Excel (.xlsx) con contenido." }
                });
            }

            using var stream = archivoExcel!.OpenReadStream();
            var v = await _excelService.ValidarArchivoExcelAsync(stream);

            return Json(new
            {
                valido = v.IsValid,
                tipoValido = v.ArchivoLegible,
                estructuraValida = v.EstructuraValida,
                filasConCantidadInvalida = v.FilasConCantidadInvalida,
                filasConBodegaNoRegistrada = v.FilasConBodegaNoRegistrada,
                filasConUbicacionNoRegistrada = v.FilasConUbicacionNoRegistrada,
                filasConProductoIncorrecto = v.FilasConProductoIncorrecto,
                filasConPedidoDuplicado = v.FilasConPedidoDuplicado,
                totalPedidos = v.TotalPedidos,
                totalFilas = v.TotalFilas,
                errores = v.Errors
            });
        }

        // Descargar Plantilla Excel
        [Authorize(Roles = "Administrador,Supervisor")]
        [HttpGet]
        public IActionResult DescargarPlantilla()
        {
            var bytes = _excelService.GenerarPlantillaExcel();
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "Plantilla_Pedidos_Picking.xlsx");
        }

        // Slide 8 / CU-26: Asignar pedido a empleado (Vista interactiva)
        [Authorize(Roles = "Administrador,Supervisor")]
        [HttpGet]
        public async Task<IActionResult> Asignar(int id)
        {
            var pedido = await _context.Pedidos
                .Include(p => p.Bodega)
                .Include(p => p.Detalles)
                .ThenInclude(d => d.Ubicacion)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (pedido == null) return NotFound();

            if (pedido.EstaDividido)
            {
                TempData["ErrorMessage"] = "El pedido fue dividido en partes: asigne cada parte por separado.";
                return RedirectToAction(nameof(Detalle), new { id });
            }

            // CU-27: si el pedido ya está en curso con un empleado, esta vista sirve para reasignarlo
            var empleadoActualId = pedido.EstaEnCurso ? pedido.EmpleadoId : null;

            // MesesExperiencia es [NotMapped] (propiedad calculada en C#), por lo que
            // no puede ser traducida a SQL por EF Core. Se trae primero a memoria
            // y luego se ordena en el lado del cliente.
            var empleados = (await _context.Empleados
                .Where(e => e.Activo)
                .ToListAsync())
                .OrderByDescending(e => e.MesesExperiencia)
                .ToList();

            // Tiempo estimado de cada empleado = tiempo estándar del pedido × su factor de rendimiento
            var tiempoEstandar = await _pickingEngine.CalcularTiempoEstandarAsync(id);
            var rendimientos = await _pickingEngine.CalcularRendimientosAsync(empleados.Select(e => e.Id).ToList());

            var tiemposEstimados = new Dictionary<int, double>();
            var motivosNoAsignable = new Dictionary<int, string>();
            foreach (var emp in empleados)
            {
                tiemposEstimados[emp.Id] = rendimientos[emp.Id].Estimar(tiempoEstandar);

                if (emp.Id == empleadoActualId)
                {
                    motivosNoAsignable[emp.Id] = "Asignado actualmente";
                    continue;
                }

                // Mismas reglas que se aplican al asignar (activo, pedido en curso, jornada, tiempo restante)
                var validacion = await _pickingEngine.ValidarAsignacionAsync(id, emp.Id);
                if (!validacion.PuedeAsignar)
                {
                    motivosNoAsignable[emp.Id] = validacion.MotivoCorto ?? "No disponible";
                }
            }

            // Primero los que pueden recibir el pedido, por menor tiempo estimado; el recomendado es el primero de ellos
            empleados = empleados
                .OrderBy(e => motivosNoAsignable.ContainsKey(e.Id))
                .ThenBy(e => motivosNoAsignable.ContainsKey(e.Id) ? 0 : tiemposEstimados[e.Id])
                .ThenByDescending(e => e.MesesExperiencia)
                .ToList();

            ViewBag.Pedido = pedido;
            ViewBag.TiemposEstimados = tiemposEstimados;
            ViewBag.TiempoEstandar = tiempoEstandar;
            ViewBag.Rendimientos = rendimientos;
            ViewBag.MotivosNoAsignable = motivosNoAsignable;
            ViewBag.EmpleadoRecomendadoId = empleados.FirstOrDefault(e => !motivosNoAsignable.ContainsKey(e.Id))?.Id;
            ViewBag.MesesExperienciaAdvertencia = await ParametrosSistema.ObtenerAsync(_context, ParametrosSistema.MesesExperienciaAdvertencia);
            ViewBag.MaxUbicacionesAdvertencia = await ParametrosSistema.ObtenerAsync(_context, ParametrosSistema.MaxUbicacionesAdvertencia);
            ViewBag.EmpleadoActualId = empleadoActualId;
            ViewBag.EmpleadoActualNombre = empleados.FirstOrDefault(e => e.Id == empleadoActualId)?.NombreCompleto
                ?? (empleadoActualId.HasValue ? (await _context.Empleados.FindAsync(empleadoActualId.Value))?.NombreCompleto : null);
            return View(empleados);
        }

        // CU-26: Asignar pedido a empleado
        [Authorize(Roles = "Administrador,Supervisor")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Asignar(int pedidoId, int empleadoId, bool ignorarAdvertencia = false)
        {
            var pedido = await _context.Pedidos.FindAsync(pedidoId);
            if (pedido == null) return NotFound();

            // Un pedido en curso solo cambia de empleado por CU-27 (motivo obligatorio y liberación del anterior)
            if (pedido.EstaEnCurso)
            {
                TempData["ErrorMessage"] = "El pedido ya tiene un empleado asignado. Use la opción Reasignar e indique el motivo.";
                return RedirectToAction(nameof(Detalle), new { id = pedidoId });
            }

            var validacion = await _pickingEngine.ValidarAsignacionAsync(pedidoId, empleadoId);

            if (!validacion.PuedeAsignar)
            {
                TempData["ErrorMessage"] = validacion.MensajeError;
                return RedirectToAction(nameof(Detalle), new { id = pedidoId });
            }

            var empleado = await _context.Empleados.FindAsync(empleadoId);
            if (empleado == null) return NotFound();

            var userEmail = User.FindFirstValue(ClaimTypes.Email) ?? "Usuario";
            var userRole = User.FindFirstValue(ClaimTypes.Role) ?? "Usuario";

            // Si genera advertencia de experiencia (umbrales de Configuración)
            if (validacion.GeneraAdvertencia)
            {
                NotificarAdvertenciaAsignacion(validacion, pedido, empleado, esReasignacion: false);
            }

            pedido.Asignar(empleadoId, DateTime.Now.AddMinutes(await ObtenerMargenInicioAsync()),
                validacion.TiempoEstimadoMinutos, validacion.TiempoEstandarMinutos);

            await _context.SaveChangesAsync();

            if (validacion.GeneraAdvertencia)
            {
                await RegistrarAdvertenciaAsignacionAsync(validacion, pedido, esReasignacion: false, userEmail, userRole);
            }
            await _auditService.LogAsync(userEmail, userRole, "Asignar Pedido", "Pedidos", 
                $"Pedido {pedido.CodigoPedido} asignado al empleado {empleado.NombreCompleto}. Estimado: {Formato.Duracion(validacion.TiempoEstimadoMinutos)} (estándar {Formato.Duracion(validacion.TiempoEstandarMinutos)} × rendimiento {validacion.Rendimiento?.Factor ?? 1:0.00})");

            if (validacion.GeneraAdvertencia)
            {
                TempData["WarningMessage"] = validacion.MensajeAdvertencia;
            }
            TempData["SuccessMessage"] = $"Pedido '{pedido.CodigoPedido}' asignado exitosamente a '{empleado.NombreCompleto}'.";

            return RedirectToAction(nameof(Detalle), new { id = pedidoId });
        }

        // CU-27: Reasignar pedido a otro empleado
        [Authorize(Roles = "Administrador,Supervisor")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Reasignar(int pedidoId, int nuevoEmpleadoId, string motivoReasignacion)
        {
            var motivo = motivoReasignacion?.Trim() ?? "";
            if (motivo.Length == 0)
            {
                TempData["ErrorMessage"] = "El motivo de reasignación es estrictamente obligatorio.";
                return RedirectToAction(nameof(Asignar), new { id = pedidoId });
            }
            if (motivo.Length > MaxLongitudMotivoReasignacion)
            {
                TempData["ErrorMessage"] = $"El motivo de reasignación no puede superar {MaxLongitudMotivoReasignacion} caracteres.";
                return RedirectToAction(nameof(Asignar), new { id = pedidoId });
            }

            var pedido = await _context.Pedidos.FindAsync(pedidoId);
            if (pedido == null) return NotFound();

            if (!pedido.EstaEnCurso || !pedido.EmpleadoId.HasValue)
            {
                TempData["ErrorMessage"] = "Solo se puede reasignar un pedido que esté asignado a un empleado.";
                return RedirectToAction(nameof(Detalle), new { id = pedidoId });
            }

            if (pedido.EmpleadoId == nuevoEmpleadoId)
            {
                TempData["ErrorMessage"] = "El pedido ya está asignado a ese empleado. Seleccione un empleado distinto.";
                return RedirectToAction(nameof(Asignar), new { id = pedidoId });
            }

            var validacion = await _pickingEngine.ValidarAsignacionAsync(pedidoId, nuevoEmpleadoId);
            if (!validacion.PuedeAsignar)
            {
                TempData["ErrorMessage"] = validacion.MensajeError;
                return RedirectToAction(nameof(Asignar), new { id = pedidoId });
            }

            var nuevoEmpleado = await _context.Empleados.FindAsync(nuevoEmpleadoId);
            if (nuevoEmpleado == null) return NotFound();

            var nombreEmpleadoAnterior = await _context.Empleados
                .Where(e => e.Id == pedido.EmpleadoId.Value)
                .Select(e => e.NombreCompleto)
                .FirstOrDefaultAsync();

            var userEmail = User.FindFirstValue(ClaimTypes.Email) ?? "Usuario";
            var userRole = User.FindFirstValue(ClaimTypes.Role) ?? "Usuario";

            if (validacion.GeneraAdvertencia)
            {
                NotificarAdvertenciaAsignacion(validacion, pedido, nuevoEmpleado, esReasignacion: true);
            }

            pedido.Reasignar(nuevoEmpleadoId, motivo, DateTime.Now.AddMinutes(await ObtenerMargenInicioAsync()),
                validacion.TiempoEstimadoMinutos, validacion.TiempoEstandarMinutos);

            await _context.SaveChangesAsync();

            if (validacion.GeneraAdvertencia)
            {
                await RegistrarAdvertenciaAsignacionAsync(validacion, pedido, esReasignacion: true, userEmail, userRole);
            }
            await _auditService.LogAsync(userEmail, userRole, "Reasignar Pedido", "Pedidos", 
                $"Pedido {pedido.CodigoPedido} reasignado de {nombreEmpleadoAnterior ?? "(sin empleado)"} a {nuevoEmpleado.NombreCompleto}. Estimado: {Formato.Duracion(validacion.TiempoEstimadoMinutos)}. Motivo: {motivo}");

            if (validacion.GeneraAdvertencia)
            {
                TempData["WarningMessage"] = validacion.MensajeAdvertencia;
            }
            TempData["SuccessMessage"] = $"Pedido '{pedido.CodigoPedido}' reasignado exitosamente a '{nuevoEmpleado.NombreCompleto}'.";
            return RedirectToAction(nameof(Detalle), new { id = pedidoId });
        }

        // Minutos entre la asignación y el inicio efectivo del picking (parámetro MargenInicioMinutos)
        private Task<int> ObtenerMargenInicioAsync() =>
            ParametrosSistema.ObtenerAsync(_context, ParametrosSistema.MargenInicioMinutos);

        private const int MaxLongitudMotivoReasignacion = 250; // varchar(250) en Pedidos.MotivoReasignacion

        // CU-26 / CU-27: advertencia de experiencia (umbrales configurables); no bloquea la asignación.
        // La notificación se agrega antes del SaveChanges para guardarse junto con la asignación;
        // el LOG se escribe después, solo si la asignación quedó guardada.
        private void NotificarAdvertenciaAsignacion(AsignacionValidationResult validacion, Pedido pedido, Empleado empleado, bool esReasignacion)
        {
            _context.Notificaciones.Add(new Notificacion
            {
                RolDestino = "Administrador",
                Titulo = "Alerta de Asignación de Picking",
                Mensaje = $"El {User.DescripcionParaNotificacion()} {(esReasignacion ? "reasignó" : "asignó")} el pedido {pedido.CodigoPedido} al empleado {empleado.NombreCompleto} ({validacion.MotivoAdvertencia}).",
                Tipo = "Warning",
                FechaCreacion = DateTime.Now
            });
        }

        private Task RegistrarAdvertenciaAsignacionAsync(AsignacionValidationResult validacion, Pedido pedido,
            bool esReasignacion, string userEmail, string userRole) =>
            _auditService.LogAsync(userEmail, userRole, "Advertencia de Asignación", "Pedidos",
                $"{validacion.MensajeAdvertencia} - {(esReasignacion ? "Reasignado" : "Asignado")} por {User.FindFirstValue(ClaimTypes.Name)} ({userRole}) al pedido {pedido.CodigoPedido}");

        // CU-28: Marcar pedido como completado
        [Authorize(Roles = "Administrador,Supervisor")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarcarCompletado(int pedidoId, bool confirmacionFisica)
        {
            if (!confirmacionFisica)
            {
                TempData["ErrorMessage"] = "El pedido no puede marcarse como completado porque no existe confirmación física de la entrega (FE-1-CU28).";
                return RedirectToAction(nameof(Detalle), new { id = pedidoId });
            }

            var pedido = await _context.Pedidos.FindAsync(pedidoId);
            if (pedido == null) return NotFound();

            if (!pedido.EstaEnCurso)
            {
                TempData["ErrorMessage"] = "Solo se puede completar un pedido que esté asignado a un empleado.";
                return RedirectToAction(nameof(Detalle), new { id = pedidoId });
            }

            var fechaFin = DateTime.Now;
            // El tiempo real cuenta desde el inicio fijado al asignar (asignación + margen de ese momento)
            var inicioProceso = pedido.FechaInicioProceso
                ?? (pedido.FechaAsignacion ?? fechaFin.AddMinutes(-10)).AddMinutes(await ObtenerMargenInicioAsync());
            var tiempoReal = _pickingEngine.CalcularTiempoProcesamiento(inicioProceso, fechaFin);

            pedido.Estado = PedidoEstados.Completado;
            pedido.ConfirmacionFisica = true;
            pedido.FechaFinalizacion = fechaFin;
            pedido.TiempoProcesamientoMinutos = tiempoReal;

            var padreCerrado = await CerrarPedidoDivididoSiTerminoAsync(pedido.PedidoPadreId);

            var userEmail = User.FindFirstValue(ClaimTypes.Email) ?? "Usuario";
            var userRole = User.FindFirstValue(ClaimTypes.Role) ?? "Usuario";

            await _context.SaveChangesAsync();

            await _auditService.LogAsync(userEmail, userRole, "Completar Pedido", "Pedidos",
                $"Pedido {pedido.CodigoPedido} marcado como completado con entrega física. Tiempo real calculado: {tiempoReal} min");
            await RegistrarCierrePedidoDivididoAsync(padreCerrado, userEmail, userRole);

            TempData["SuccessMessage"] = $"Pedido '{pedido.CodigoPedido}' completado exitosamente. Tiempo de procesamiento: {tiempoReal} minutos.";
            return RedirectToAction(nameof(Detalle), new { id = pedidoId });
        }

        // CU-31: Cancelar pedido
        [Authorize(Roles = "Administrador,Supervisor")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Cancelar(int pedidoId, string motivoCancelacion)
        {
            if (string.IsNullOrWhiteSpace(motivoCancelacion))
            {
                TempData["ErrorMessage"] = "El motivo de cancelación es obligatorio.";
                return RedirectToAction(nameof(Detalle), new { id = pedidoId });
            }

            var pedido = await _context.Pedidos.FindAsync(pedidoId);
            if (pedido == null) return NotFound();

            if (pedido.Estado == PedidoEstados.Cancelado)
            {
                TempData["ErrorMessage"] = "El pedido ya se encuentra cancelado y no puede reactivarse (RNF-01).";
                return RedirectToAction(nameof(Detalle), new { id = pedidoId });
            }

            if (pedido.Estado == PedidoEstados.Completado)
            {
                TempData["ErrorMessage"] = "El pedido ya se encuentra completado y no puede cancelarse.";
                return RedirectToAction(nameof(Detalle), new { id = pedidoId });
            }

            // Pedido dividido: se cancelan también las partes que no hayan terminado; las completadas se conservan
            var partesCanceladas = 0;
            if (pedido.EstaDividido)
            {
                var partesActivas = await _context.Pedidos
                    .Where(pa => pa.PedidoPadreId == pedido.Id && !PedidoEstados.Finalizados.Contains(pa.Estado))
                    .ToListAsync();
                foreach (var parte in partesActivas)
                {
                    parte.Estado = PedidoEstados.Cancelado;
                    parte.MotivoCancelacion = $"Cancelado junto con el pedido {pedido.CodigoPedido}: {motivoCancelacion.Trim()}";
                    if (parte.MotivoCancelacion.Length > 250) parte.MotivoCancelacion = parte.MotivoCancelacion[..250];
                    parte.FechaFinalizacion = DateTime.Now;
                }
                partesCanceladas = partesActivas.Count;
            }

            pedido.Estado = PedidoEstados.Cancelado;
            pedido.MotivoCancelacion = motivoCancelacion.Trim();
            pedido.FechaFinalizacion = DateTime.Now;

            var padreCerrado = await CerrarPedidoDivididoSiTerminoAsync(pedido.PedidoPadreId);

            var userEmail = User.FindFirstValue(ClaimTypes.Email) ?? "Usuario";
            var userRole = User.FindFirstValue(ClaimTypes.Role) ?? "Usuario";

            await _context.SaveChangesAsync();

            await _auditService.LogAsync(userEmail, userRole, "Cancelar Pedido", "Pedidos",
                $"Pedido {pedido.CodigoPedido} cancelado{(partesCanceladas > 0 ? $" junto con {partesCanceladas} parte(s) sin terminar" : "")}. Motivo: {motivoCancelacion}");
            await RegistrarCierrePedidoDivididoAsync(padreCerrado, userEmail, userRole);

            TempData["SuccessMessage"] = partesCanceladas > 0
                ? $"Pedido '{pedido.CodigoPedido}' y {partesCanceladas} parte(s) sin terminar cancelados correctamente."
                : $"Pedido '{pedido.CodigoPedido}' cancelado correctamente.";
            return RedirectToAction(nameof(Detalle), new { id = pedidoId });
        }

        // CU-29: Registrar novedad al pedido
        [Authorize(Roles = "Administrador,Supervisor")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RegistrarNovedad(int pedidoId, string tipoNovedad, string descripcion)
        {
            if (string.IsNullOrWhiteSpace(descripcion))
            {
                TempData["ErrorMessage"] = "La descripción de la novedad es obligatoria.";
                return RedirectToAction(nameof(Detalle), new { id = pedidoId });
            }

            var pedido = await _context.Pedidos.FindAsync(pedidoId);
            if (pedido == null) return NotFound();

            var userEmail = User.FindFirstValue(ClaimTypes.Email) ?? "Usuario";
            var userName = User.FindFirstValue(ClaimTypes.Name) ?? userEmail;
            var userRole = User.FindFirstValue(ClaimTypes.Role) ?? "Usuario";

            var novedad = new NovedadPedido
            {
                PedidoId = pedidoId,
                TipoNovedad = tipoNovedad,
                Descripcion = descripcion.Trim(),
                FechaRegistro = DateTime.Now,
                RegistradoPor = userName,
                Atendida = false
            };

            _context.NovedadesPedido.Add(novedad);

            _context.Notificaciones.Add(new Notificacion
            {
                RolDestino = "Administrador",
                Titulo = "Novedad en Pedido",
                Mensaje = $"Se registró una novedad de tipo '{tipoNovedad}' en el pedido {pedido.CodigoPedido}.",
                Tipo = "Warning",
                FechaCreacion = DateTime.Now,
                Enlace = $"/Pedidos/Detalle/{pedidoId}"
            });

            await _context.SaveChangesAsync();

            await _auditService.LogAsync(userEmail, userRole, "Registrar Novedad", "Pedidos", 
                $"Novedad registrada en pedido {pedido.CodigoPedido}: [{tipoNovedad}] {descripcion}");

            TempData["SuccessMessage"] = "Novedad registrada correctamente en el pedido.";
            return RedirectToAction(nameof(Detalle), new { id = pedidoId });
        }

        // CU-30: Atender novedad (Solo Administrador)
        [Authorize(Roles = "Administrador")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AtenderNovedad(int novedadId, string observacionesAtencion)
        {
            var novedad = await _context.NovedadesPedido
                .Include(n => n.Pedido)
                .FirstOrDefaultAsync(n => n.Id == novedadId);

            if (novedad == null) return NotFound();

            var userEmail = User.FindFirstValue(ClaimTypes.Email) ?? "Admin";
            var userName = User.FindFirstValue(ClaimTypes.Name) ?? userEmail;

            novedad.Atendida = true;
            novedad.AtendidaPor = userName;
            novedad.FechaAtencion = DateTime.Now;
            novedad.ObservacionesAtencion = observacionesAtencion?.Trim() ?? "Atendida y solventada";

            await _context.SaveChangesAsync();

            await _auditService.LogAsync(userEmail, "Administrador", "Atender Novedad", "Pedidos", 
                $"Novedad #{novedad.Id} del pedido {novedad.Pedido?.CodigoPedido} marcada como atendida.");

            TempData["SuccessMessage"] = "La novedad ha sido marcada como atendida.";
            return RedirectToAction(nameof(Detalle), new { id = novedad.PedidoId });
        }

        // CU-25: Cambiar prioridad del pedido (Solo Administrador)
        [Authorize(Roles = "Administrador")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CambiarPrioridad(int pedidoId, string nuevaPrioridad)
        {
            var pedido = await _context.Pedidos.FindAsync(pedidoId);
            if (pedido == null) return NotFound();

            var prioridadAnterior = pedido.Prioridad;
            pedido.Prioridad = nuevaPrioridad;

            if (pedido.EstaDividido)
            {
                await _context.Pedidos
                    .Where(pa => pa.PedidoPadreId == pedido.Id && !PedidoEstados.Finalizados.Contains(pa.Estado))
                    .ForEachAsync(pa => pa.Prioridad = nuevaPrioridad);
            }

            var userEmail = User.FindFirstValue(ClaimTypes.Email) ?? "Admin";

            await _context.SaveChangesAsync();

            await _auditService.LogAsync(userEmail, "Administrador", "Cambiar Prioridad Pedido", "Pedidos", 
                $"Prioridad del pedido {pedido.CodigoPedido} modificada de {prioridadAnterior} a {nuevaPrioridad}");

            TempData["SuccessMessage"] = $"Prioridad cambiada a '{nuevaPrioridad}' para el pedido {pedido.CodigoPedido}.";
            return RedirectToAction(nameof(Detalle), new { id = pedidoId });
        }
    

        private const int MaxPartes = 20;

        // Dividir un pedido grande en partes que se asignan por separado (p. ej. cuando supera una jornada)
        [Authorize(Roles = "Administrador,Supervisor")]
        [HttpGet]
        public async Task<IActionResult> Dividir(int id)
        {
            var pedido = await _context.Pedidos
                .Include(p => p.Bodega)
                .Include(p => p.Detalles)
                    .ThenInclude(d => d.Ubicacion)
                        .ThenInclude(u => u!.Producto)
                            .ThenInclude(pr => pr!.Categoria)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (pedido == null) return NotFound();

            var error = pedido.MotivoNoDivisible();
            if (error != null)
            {
                TempData["ErrorMessage"] = error;
                return RedirectToAction(nameof(Detalle), new { id });
            }

            var tiempoEstandar = await _pickingEngine.CalcularTiempoEstandarAsync(id);
            var jornadaMinutos = await ObtenerMaxHorasJornadaAsync() * 60;
            var maxPartes = Math.Min(MaxPartes, pedido.Detalles.Sum(d => d.Cantidad));

            ViewBag.TiempoEstandar = tiempoEstandar;
            ViewBag.JornadaMinutos = jornadaMinutos;
            ViewBag.MinutosPredeterminados = await _pickingEngine.ObtenerMinutosPredeterminadosAsync();
            ViewBag.MinutosPorUbicacion = await _pickingEngine.ObtenerMinutosPorUbicacionAsync();
            ViewBag.MaxPartes = maxPartes;
            // Sugerencia: las partes necesarias para que cada una quepa en una jornada a ritmo estándar
            ViewBag.PartesSugeridas = Math.Clamp((int)Math.Ceiling(tiempoEstandar / jornadaMinutos), 2, maxPartes);
            return View(pedido);
        }

        // cantidades["{detalleId}_{parte}"] = unidades de esa línea que van en esa parte
        [Authorize(Roles = "Administrador,Supervisor")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Dividir(int pedidoId, int partes, Dictionary<string, string>? cantidades)
        {
            var pedido = await _context.Pedidos
                .Include(p => p.Detalles)
                .FirstOrDefaultAsync(p => p.Id == pedidoId);

            if (pedido == null) return NotFound();

            var error = pedido.MotivoNoDivisible();
            if (error != null)
            {
                TempData["ErrorMessage"] = error;
                return RedirectToAction(nameof(Detalle), new { id = pedidoId });
            }

            var maxPartes = Math.Min(MaxPartes, pedido.Detalles.Sum(d => d.Cantidad));
            if (partes < 2 || partes > maxPartes)
            {
                return ErrorDivision(pedidoId, $"El número de partes debe estar entre 2 y {maxPartes}.");
            }

            cantidades ??= new Dictionary<string, string>();
            var detalles = pedido.Detalles.OrderBy(d => d.Id).ToList();
            var reparto = new Dictionary<(int DetalleId, int Parte), int>();

            foreach (var d in detalles)
            {
                var suma = 0;
                for (var k = 1; k <= partes; k++)
                {
                    var texto = cantidades.GetValueOrDefault($"{d.Id}_{k}");
                    var unidades = 0;
                    if (!string.IsNullOrWhiteSpace(texto) && (!int.TryParse(texto, out unidades) || unidades < 0))
                    {
                        return ErrorDivision(pedidoId, $"La cantidad de «{d.NombreProducto}» en la parte {k} no es válida.");
                    }
                    reparto[(d.Id, k)] = unidades;
                    suma += unidades;
                }
                if (suma != d.Cantidad)
                {
                    return ErrorDivision(pedidoId, $"«{d.NombreProducto}» tiene {d.Cantidad} unidades y se repartieron {suma}.");
                }
            }

            for (var k = 1; k <= partes; k++)
            {
                if (detalles.All(d => reparto[(d.Id, k)] == 0))
                {
                    return ErrorDivision(pedidoId, $"La parte {k} quedó sin productos.");
                }
            }

            var codigos = Enumerable.Range(1, partes).Select(k => $"{pedido.CodigoPedido}-P{k}").ToList();
            if (codigos.Any(c => c.Length > 50) || await _context.Pedidos.AnyAsync(p => codigos.Contains(p.CodigoPedido)))
            {
                return ErrorDivision(pedidoId, "Ya existen pedidos con los códigos de las partes; revise los pedidos cargados.");
            }

            var resumen = new List<string>();
            for (var k = 1; k <= partes; k++)
            {
                var parte = new Pedido
                {
                    CodigoPedido = codigos[k - 1],
                    BodegaId = pedido.BodegaId,
                    Prioridad = pedido.Prioridad,
                    Estado = PedidoEstados.Pendiente,
                    FechaCarga = pedido.FechaCarga,
                    PedidoPadreId = pedido.Id,
                    NumeroParte = k,
                    Detalles = detalles
                        .Where(d => reparto[(d.Id, k)] > 0)
                        .Select(d => new DetallePedido
                        {
                            CodigoProducto = d.CodigoProducto,
                            NombreProducto = d.NombreProducto,
                            Cantidad = reparto[(d.Id, k)],
                            UbicacionId = d.UbicacionId
                        })
                        .ToList()
                };
                _context.Pedidos.Add(parte);
                resumen.Add($"{parte.CodigoPedido} ({parte.Detalles.Sum(d => d.Cantidad)} uds)");
            }

            pedido.Estado = PedidoEstados.Dividido;
            await _context.SaveChangesAsync();

            var userEmail = User.FindFirstValue(ClaimTypes.Email) ?? "Usuario";
            var userRole = User.FindFirstValue(ClaimTypes.Role) ?? "Usuario";
            await _auditService.LogAsync(userEmail, userRole, "Dividir Pedido", "Pedidos",
                $"Pedido {pedido.CodigoPedido} dividido en {partes} partes: {string.Join(", ", resumen)}.");

            TempData["SuccessMessage"] = $"Pedido '{pedido.CodigoPedido}' dividido en {partes} partes. Ahora puede asignar cada parte.";
            return RedirectToAction(nameof(Detalle), new { id = pedidoId });
        }

        // Volver a unir las partes mientras ninguna se haya asignado ni tenga novedades
        [Authorize(Roles = "Administrador,Supervisor")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeshacerDivision(int pedidoId)
        {
            var pedido = await _context.Pedidos
                .Include(p => p.Partes)
                    .ThenInclude(pa => pa.Novedades)
                .FirstOrDefaultAsync(p => p.Id == pedidoId);

            if (pedido == null) return NotFound();

            if (pedido.Estado != PedidoEstados.Dividido)
            {
                TempData["ErrorMessage"] = "El pedido no está dividido.";
                return RedirectToAction(nameof(Detalle), new { id = pedidoId });
            }

            if (!pedido.PuedeDeshacerDivision)
            {
                TempData["ErrorMessage"] = "No se puede deshacer la división: alguna parte ya fue asignada, finalizada o tiene novedades.";
                return RedirectToAction(nameof(Detalle), new { id = pedidoId });
            }

            var totalPartes = pedido.Partes.Count;
            _context.Pedidos.RemoveRange(pedido.Partes);
            pedido.Estado = PedidoEstados.Pendiente;
            await _context.SaveChangesAsync();

            var userEmail = User.FindFirstValue(ClaimTypes.Email) ?? "Usuario";
            var userRole = User.FindFirstValue(ClaimTypes.Role) ?? "Usuario";
            await _auditService.LogAsync(userEmail, userRole, "Deshacer División", "Pedidos",
                $"Se unieron de nuevo las {totalPartes} partes del pedido {pedido.CodigoPedido}.");

            TempData["SuccessMessage"] = $"Se deshizo la división: el pedido '{pedido.CodigoPedido}' vuelve a estar pendiente como un solo pedido.";
            return RedirectToAction(nameof(Detalle), new { id = pedidoId });
        }

        private IActionResult ErrorDivision(int pedidoId, string mensaje)
        {
            TempData["ErrorMessage"] = mensaje;
            return RedirectToAction(nameof(Dividir), new { id = pedidoId });
        }

        // Solo modifica el contexto (la parte recién finalizada ya está rastreada con su nuevo estado), así el
        // cierre se guarda en el mismo SaveChanges que la parte. Devuelve el pedido original si se cerró.
        private async Task<Pedido?> CerrarPedidoDivididoSiTerminoAsync(int? pedidoPadreId)
        {
            if (pedidoPadreId == null) return null;

            var padre = await _context.Pedidos
                .Include(p => p.Partes)
                .FirstOrDefaultAsync(p => p.Id == pedidoPadreId);

            return padre != null && padre.CerrarSiTerminaronLasPartes() ? padre : null;
        }

        private async Task RegistrarCierrePedidoDivididoAsync(Pedido? padre, string userEmail, string userRole)
        {
            if (padre == null) return;

            var completado = padre.Estado == PedidoEstados.Completado;
            await _auditService.LogAsync(userEmail, userRole, completado ? "Completar Pedido" : "Cancelar Pedido", "Pedidos",
                $"Pedido {padre.CodigoPedido} {(completado ? "completado" : "cancelado")} automáticamente: finalizaron sus {padre.Partes.Count} partes.");
        }

        private async Task<double> ObtenerMaxHorasJornadaAsync()
        {
            var cfg = await _context.ConfiguracionesSistema.FirstOrDefaultAsync(c => c.Clave == "MaxHorasJornada");
            return cfg != null && Formato.TryParseDecimal(cfg.Valor, out var horas) && horas > 0 ? horas : 9.0;
        }
    }
}
