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
        public async Task<IActionResult> Index(string? estado, string? prioridad, int? bodegaId, string? busqueda,
            int? empleadoId, DateTime? fechaDesde, DateTime? fechaHasta)
        {
            var query = _context.Pedidos
                .Include(p => p.Bodega)
                .Include(p => p.Empleado)
                .Include(p => p.Detalles)
                .AsQueryable();

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

            // empleadoId = -1 filtra los pedidos sin empleado asignado
            if (empleadoId == -1)
            {
                query = query.Where(p => p.EmpleadoId == null);
            }
            else if (empleadoId.HasValue && empleadoId.Value > 0)
            {
                query = query.Where(p => p.EmpleadoId == empleadoId.Value);
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

            var pedidos = await query
                .OrderByDescending(p => p.Prioridad == "Alta")
                .ThenByDescending(p => p.FechaCarga)
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
                .FirstOrDefaultAsync(p => p.Id == id);

            if (pedido == null) return NotFound();

            // Tiempo estándar según las categorías de los productos, comparado con una jornada completa
            ViewBag.TiempoEstandar = pedido.TiempoEstandarMinutos ?? await _pickingEngine.CalcularTiempoEstandarAsync(pedido.Id);
            ViewBag.CategoriaPredeterminada = await _context.CategoriasProducto
                .Where(c => c.EsPredeterminada).Select(c => c.Nombre).FirstOrDefaultAsync();
            var cfgJornada = await _context.ConfiguracionesSistema.FirstOrDefaultAsync(c => c.Clave == "MaxHorasJornada");
            ViewBag.MaxHorasJornada = cfgJornada != null && Formato.TryParseDecimal(cfgJornada.Valor, out var horas) ? horas : 9.0;

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

            foreach (var pedido in validacion.PedidosValidados)
            {
                // Verificar si ya existe el código de pedido para no duplicar
                if (await _context.Pedidos.AnyAsync(p => p.CodigoPedido == pedido.CodigoPedido))
                {
                    pedido.CodigoPedido = $"{pedido.CodigoPedido}-{DateTime.Now:HHmmss}";
                }
                _context.Pedidos.Add(pedido);
            }

            await _context.SaveChangesAsync();

            await _auditService.LogAsync(userEmail, userRole, "Carga Excel Pedidos", "Pedidos", 
                $"Se procesó el archivo '{archivoExcel.FileName}' registrando {validacion.TotalPedidos} pedidos ({validacion.TotalFilas} líneas de productos).");

            TempData["SuccessMessage"] = $"El archivo fue procesado correctamente y los {validacion.TotalPedidos} pedidos quedaron registrados en el sistema.";
            return RedirectToAction(nameof(Index));
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

            // MesesExperiencia es [NotMapped] (propiedad calculada en C#), por lo que
            // no puede ser traducida a SQL por EF Core. Se trae primero a memoria
            // y luego se ordena en el lado del cliente.
            var empleados = (await _context.Empleados
                .Where(e => e.Activo)
                .ToListAsync())
                .OrderByDescending(e => e.EstadoDisponibilidad == "Disponible")
                .ThenByDescending(e => e.MesesExperiencia)
                .ToList();

            // Tiempo estimado de cada empleado = tiempo estándar del pedido × su factor de rendimiento
            var tiempoEstandar = await _pickingEngine.CalcularTiempoEstandarAsync(id);
            var rendimientos = await _pickingEngine.CalcularRendimientosAsync(empleados.Select(e => e.Id).ToList());

            var tiemposEstimados = new Dictionary<int, double>();
            var motivosNoAsignable = new Dictionary<int, string>();
            foreach (var emp in empleados)
            {
                tiemposEstimados[emp.Id] = rendimientos[emp.Id].Estimar(tiempoEstandar);

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
            return View(empleados);
        }

        // CU-26: Asignar pedido a empleado
        [Authorize(Roles = "Administrador,Supervisor")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Asignar(int pedidoId, int empleadoId, bool ignorarAdvertencia = false)
        {
            var validacion = await _pickingEngine.ValidarAsignacionAsync(pedidoId, empleadoId);

            if (!validacion.PuedeAsignar)
            {
                TempData["ErrorMessage"] = validacion.MensajeError;
                return RedirectToAction(nameof(Detalle), new { id = pedidoId });
            }

            var pedido = await _context.Pedidos.FindAsync(pedidoId);
            var empleado = await _context.Empleados.FindAsync(empleadoId);

            if (pedido == null || empleado == null) return NotFound();

            var userEmail = User.FindFirstValue(ClaimTypes.Email) ?? "Usuario";
            var userRole = User.FindFirstValue(ClaimTypes.Role) ?? "Usuario";

            // Si genera advertencia de experiencia (< 6 meses y > 10 ubicaciones)
            if (validacion.GeneraAdvertencia)
            {
                await _auditService.LogAsync(userEmail, userRole, "Advertencia de Asignación", "Pedidos", 
                    $"{validacion.MensajeAdvertencia} - Asignado por {User.FindFirstValue(ClaimTypes.Name)} ({userRole}) al pedido {pedido.CodigoPedido}");

                _context.Notificaciones.Add(new Notificacion
                {
                    RolDestino = "Administrador",
                    Titulo = "Alerta de Asignación de Picking",
                    Mensaje = $"El {User.DescripcionParaNotificacion()} asignó el pedido {pedido.CodigoPedido} al empleado {empleado.NombreCompleto} (menos de 6 meses de experiencia con más de 10 ubicaciones).",
                    Tipo = "Warning",
                    FechaCreacion = DateTime.Now
                });
            }

            pedido.EmpleadoId = empleadoId;
            pedido.Estado = "Asignado";
            pedido.FechaAsignacion = DateTime.Now;
            pedido.FechaInicioProceso = DateTime.Now.AddMinutes(2); // Margen de 2 minutos reglamentario
            pedido.TiempoEstimadoMinutos = validacion.TiempoEstimadoMinutos;
            pedido.TiempoEstandarMinutos = validacion.TiempoEstandarMinutos;

            empleado.EstadoDisponibilidad = "EnPicking";

            await _context.SaveChangesAsync();

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
            if (string.IsNullOrWhiteSpace(motivoReasignacion))
            {
                TempData["ErrorMessage"] = "El motivo de reasignación es estrictamente obligatorio.";
                return RedirectToAction(nameof(Detalle), new { id = pedidoId });
            }

            var pedido = await _context.Pedidos.FindAsync(pedidoId);
            if (pedido == null) return NotFound();

            var validacion = await _pickingEngine.ValidarAsignacionAsync(pedidoId, nuevoEmpleadoId);
            if (!validacion.PuedeAsignar)
            {
                TempData["ErrorMessage"] = validacion.MensajeError;
                return RedirectToAction(nameof(Detalle), new { id = pedidoId });
            }

            // Liberar empleado anterior si había uno
            if (pedido.EmpleadoId.HasValue)
            {
                var empleadoAnterior = await _context.Empleados.FindAsync(pedido.EmpleadoId.Value);
                if (empleadoAnterior != null)
                {
                    empleadoAnterior.EstadoDisponibilidad = "Disponible";
                }
            }

            var nuevoEmpleado = await _context.Empleados.FindAsync(nuevoEmpleadoId);
            if (nuevoEmpleado == null) return NotFound();

            var userEmail = User.FindFirstValue(ClaimTypes.Email) ?? "Usuario";
            var userRole = User.FindFirstValue(ClaimTypes.Role) ?? "Usuario";

            pedido.EmpleadoId = nuevoEmpleadoId;
            pedido.Estado = "Reasignado";
            pedido.MotivoReasignacion = motivoReasignacion.Trim();
            pedido.FechaAsignacion = DateTime.Now;
            pedido.FechaInicioProceso = DateTime.Now.AddMinutes(2);
            pedido.TiempoEstimadoMinutos = validacion.TiempoEstimadoMinutos;
            pedido.TiempoEstandarMinutos = validacion.TiempoEstandarMinutos;

            nuevoEmpleado.EstadoDisponibilidad = "EnPicking";

            await _context.SaveChangesAsync();

            await _auditService.LogAsync(userEmail, userRole, "Reasignar Pedido", "Pedidos", 
                $"Pedido {pedido.CodigoPedido} reasignado a {nuevoEmpleado.NombreCompleto}. Estimado: {Formato.Duracion(validacion.TiempoEstimadoMinutos)}. Motivo: {motivoReasignacion}");

            TempData["SuccessMessage"] = $"Pedido '{pedido.CodigoPedido}' reasignado exitosamente a '{nuevoEmpleado.NombreCompleto}'.";
            return RedirectToAction(nameof(Detalle), new { id = pedidoId });
        }

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

            var fechaFin = DateTime.Now;
            var tiempoReal = _pickingEngine.CalcularTiempoProcesamiento(pedido.FechaAsignacion ?? fechaFin.AddMinutes(-10), fechaFin);

            pedido.Estado = "Completado";
            pedido.ConfirmacionFisica = true;
            pedido.FechaFinalizacion = fechaFin;
            pedido.TiempoProcesamientoMinutos = tiempoReal;

            // Liberar empleado
            if (pedido.EmpleadoId.HasValue)
            {
                var empleado = await _context.Empleados.FindAsync(pedido.EmpleadoId.Value);
                if (empleado != null)
                {
                    empleado.EstadoDisponibilidad = "Disponible";
                }
            }

            var userEmail = User.FindFirstValue(ClaimTypes.Email) ?? "Usuario";
            var userRole = User.FindFirstValue(ClaimTypes.Role) ?? "Usuario";

            await _context.SaveChangesAsync();

            await _auditService.LogAsync(userEmail, userRole, "Completar Pedido", "Pedidos", 
                $"Pedido {pedido.CodigoPedido} marcado como completado con entrega física. Tiempo real calculado: {tiempoReal} min");

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

            if (pedido.Estado == "Cancelado")
            {
                TempData["ErrorMessage"] = "El pedido ya se encuentra cancelado y no puede reactivarse (RNF-01).";
                return RedirectToAction(nameof(Detalle), new { id = pedidoId });
            }

            if (pedido.Estado == "Completado")
            {
                TempData["ErrorMessage"] = "El pedido ya se encuentra completado y no puede cancelarse.";
                return RedirectToAction(nameof(Detalle), new { id = pedidoId });
            }

            // Liberar empleado si tenía uno asignado
            if (pedido.EmpleadoId.HasValue)
            {
                var empleado = await _context.Empleados.FindAsync(pedido.EmpleadoId.Value);
                if (empleado != null)
                {
                    empleado.EstadoDisponibilidad = "Disponible";
                }
            }

            pedido.Estado = "Cancelado";
            pedido.MotivoCancelacion = motivoCancelacion.Trim();
            pedido.FechaFinalizacion = DateTime.Now;

            var userEmail = User.FindFirstValue(ClaimTypes.Email) ?? "Usuario";
            var userRole = User.FindFirstValue(ClaimTypes.Role) ?? "Usuario";

            await _context.SaveChangesAsync();

            await _auditService.LogAsync(userEmail, userRole, "Cancelar Pedido", "Pedidos", 
                $"Pedido {pedido.CodigoPedido} cancelado. Motivo: {motivoCancelacion}");

            TempData["SuccessMessage"] = $"Pedido '{pedido.CodigoPedido}' cancelado correctamente.";
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

            var userEmail = User.FindFirstValue(ClaimTypes.Email) ?? "Admin";

            await _context.SaveChangesAsync();

            await _auditService.LogAsync(userEmail, "Administrador", "Cambiar Prioridad Pedido", "Pedidos", 
                $"Prioridad del pedido {pedido.CodigoPedido} modificada de {prioridadAnterior} a {nuevaPrioridad}");

            TempData["SuccessMessage"] = $"Prioridad cambiada a '{nuevaPrioridad}' para el pedido {pedido.CodigoPedido}.";
            return RedirectToAction(nameof(Detalle), new { id = pedidoId });
        }
    }
}
