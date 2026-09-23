using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickingApp.Data;
using PickingApp.Models;
using PickingApp.Services;

namespace PickingApp.Controllers
{
    [Authorize(Roles = "Administrador")]
    public class AuditoriaController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IAuditService _auditService;
        private readonly IReportService _reportService;

        public AuditoriaController(ApplicationDbContext context, IAuditService auditService, IReportService reportService)
        {
            _context = context;
            _auditService = auditService;
            _reportService = reportService;
        }

        private const int MaxRegistrosPantalla = 200;

        // CU-38: Consultar LOG del sistema
        public async Task<IActionResult> Index(string? modulo, string? usuario, DateTime? fechaDesde, DateTime? fechaHasta)
        {
            var query = FiltrarLogs(modulo, usuario, fechaDesde, fechaHasta);

            ViewBag.TotalFiltrado = await query.CountAsync();
            var logs = await query
                .OrderByDescending(l => l.FechaHora)
                .Take(MaxRegistrosPantalla)
                .ToListAsync();
            await ReemplazarCorreosEnDetalleAsync(logs);

            ViewBag.ModuloFiltro = modulo;
            ViewBag.UsuarioFiltro = usuario;
            ViewBag.FechaDesdeFiltro = fechaDesde?.ToString("yyyy-MM-dd");
            ViewBag.FechaHastaFiltro = fechaHasta?.ToString("yyyy-MM-dd");
            ViewBag.MaxRegistros = MaxRegistrosPantalla;
            ViewBag.Modulos = await _context.LogsSistema.Select(l => l.Modulo).Distinct().ToListAsync();
            ViewBag.LogHabilitado = await _auditService.IsLogEnabledAsync();

            return View(logs);
        }

        // Exportar LOG del sistema a PDF (CU-38 / CU-36): exige el rango de fechas a exportar
        public async Task<IActionResult> ExportarPdf(string? modulo, string? usuario, DateTime? fechaDesde, DateTime? fechaHasta)
        {
            if (!fechaDesde.HasValue || !fechaHasta.HasValue)
            {
                TempData["ErrorMessage"] = "Debe indicar la fecha desde y la fecha hasta del LOG que desea exportar.";
                return RedirectToAction(nameof(Index), new { modulo, usuario });
            }

            if (fechaDesde.Value.Date > fechaHasta.Value.Date)
            {
                TempData["ErrorMessage"] = "La fecha desde no puede ser posterior a la fecha hasta.";
                return RedirectToAction(nameof(Index), new { modulo, usuario });
            }

            // Se exportan todos los registros del rango (sin el límite de la pantalla)
            var logs = await FiltrarLogs(modulo, usuario, fechaDesde, fechaHasta)
                .OrderByDescending(l => l.FechaHora)
                .ToListAsync();
            await ReemplazarCorreosEnDetalleAsync(logs);

            var pdfBytes = _reportService.GenerarReporteLogSistemaPdf(logs, modulo, usuario, fechaDesde.Value.Date, fechaHasta.Value.Date);

            var userEmail = User.FindFirstValue(ClaimTypes.Email) ?? "Admin";
            await _auditService.LogAsync(userEmail, "Administrador", "Exportar LOG a PDF", "Auditoría",
                $"Se exportó el LOG del sistema a PDF del {fechaDesde.Value:dd/MM/yyyy} al {fechaHasta.Value:dd/MM/yyyy} ({logs.Count} registros).");

            return File(pdfBytes, "application/pdf", $"Log_Sistema_{fechaDesde.Value:yyyyMMdd}_{fechaHasta.Value:yyyyMMdd}.pdf");
        }

        private IQueryable<LogSistema> FiltrarLogs(string? modulo, string? usuario, DateTime? fechaDesde, DateTime? fechaHasta)
        {
            // Sin seguimiento: los registros se ajustan solo para mostrarlos y nunca se guardan
            var query = _context.LogsSistema
                .AsNoTracking()
                .Include(l => l.UsuarioAsociado)
                .AsQueryable();

            if (!string.IsNullOrEmpty(modulo))
            {
                query = query.Where(l => l.Modulo == modulo);
            }

            if (!string.IsNullOrEmpty(usuario))
            {
                var texto = usuario.Trim();
                query = query.Where(l => l.Usuario.Contains(texto)
                    || (l.UsuarioAsociado != null && l.UsuarioAsociado.NombreCompleto.Contains(texto)));
            }

            if (fechaDesde.HasValue)
            {
                var desde = fechaDesde.Value.Date;
                query = query.Where(l => l.FechaHora >= desde);
            }

            if (fechaHasta.HasValue)
            {
                var hastaExclusivo = fechaHasta.Value.Date.AddDays(1);
                query = query.Where(l => l.FechaHora < hastaExclusivo);
            }

            return query;
        }

        // Registros antiguos guardaron correos en la descripción: se muestran como "Nombre (Rol)"
        // si pertenecen a un usuario registrado. Los correos no registrados se conservan como evidencia.
        private async Task ReemplazarCorreosEnDetalleAsync(List<LogSistema> logs)
        {
            var usuarios = await _context.Usuarios.AsNoTracking()
                .Select(u => new { u.Correo, u.NombreCompleto, u.Rol })
                .ToListAsync();
            var nombresPorCorreo = usuarios.ToDictionary(u => u.Correo, u => $"{u.NombreCompleto} ({u.Rol})", StringComparer.OrdinalIgnoreCase);

            foreach (var l in logs)
            {
                l.Detalle = RegexCorreo.Replace(l.Detalle, m => nombresPorCorreo.TryGetValue(m.Value, out var nombre) ? nombre : m.Value);
            }
        }

        private static readonly System.Text.RegularExpressions.Regex RegexCorreo =
            new(@"[\w.+-]+@[\w-]+(\.[\w-]+)+", System.Text.RegularExpressions.RegexOptions.Compiled);

        // CU-39: Habilitar o deshabilitar registro de LOG y configurar parámetros
        [HttpGet]
        public async Task<IActionResult> Configuracion()
        {
            var configs = await _context.ConfiguracionesSistema.ToListAsync();
            return View(configs);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GuardarConfiguracion(Dictionary<string, string> configuraciones)
        {
            var userEmail = User.FindFirstValue(ClaimTypes.Email) ?? "Admin";

            foreach (var kvp in configuraciones)
            {
                var cfg = await _context.ConfiguracionesSistema.FirstOrDefaultAsync(c => c.Clave == kvp.Key);
                if (cfg != null)
                {
                    cfg.Valor = kvp.Value;
                }
                else
                {
                    _context.ConfiguracionesSistema.Add(new ConfiguracionSistema { Clave = kvp.Key, Valor = kvp.Value });
                }
            }

            await _context.SaveChangesAsync();

            await _auditService.LogAsync(userEmail, "Administrador", "Actualizar Configuración", "Sistema", 
                "Configuración general del sistema actualizada.");

            TempData["SuccessMessage"] = "Parámetros del sistema y auditoría guardados exitosamente.";
            return RedirectToAction(nameof(Configuracion));
        }

        // CU-40: Eliminar registros del LOG
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EliminarLogs(DateTime? hastaFecha)
        {
            var userEmail = User.FindFirstValue(ClaimTypes.Email) ?? "Admin";

            // RN-02-CU40: no se depuran registros críticos de seguridad/auditoría, sin importar su fecha,
            // para conservar la trazabilidad de accesos, bloqueos, aprobaciones y mantenimiento del sistema.
            var modulosProtegidos = new[] { "Seguridad", "Autenticación", "Auditoría", "Mantenimiento" };

            IQueryable<LogSistema> query = _context.LogsSistema
                .Where(l => !modulosProtegidos.Contains(l.Modulo));

            if (hastaFecha.HasValue)
            {
                var f = hastaFecha.Value.Date.AddDays(1).AddTicks(-1);
                query = query.Where(l => l.FechaHora <= f);
            }

            var registrosAEliminar = await query.ToListAsync();
            _context.LogsSistema.RemoveRange(registrosAEliminar);
            await _context.SaveChangesAsync();

            await _auditService.LogAsync(userEmail, "Administrador", "Depuración de LOGs", "Auditoría", 
                $"Se eliminaron {registrosAEliminar.Count} registros del historial de LOG.");

            TempData["SuccessMessage"] = $"Se han depurado {registrosAEliminar.Count} registros del LOG del sistema. Los registros de seguridad, autenticación y mantenimiento se conservaron.";
            return RedirectToAction(nameof(Index));
        }

        // CU-15: Limpiar base de datos
        [HttpGet]
        public IActionResult LimpiarBaseDatos()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConfirmarLimpiezaBaseDatos(string confirmacion, bool limpiarLog = false)
        {
            if (confirmacion != "LIMPIAR_SISTEMA")
            {
                TempData["ErrorMessage"] = "Debe escribir exactamente 'LIMPIAR_SISTEMA' para confirmar la operación destructiva.";
                return View("LimpiarBaseDatos");
            }

            var userEmail = User.FindFirstValue(ClaimTypes.Email) ?? "Admin";

            // Eliminar dependientes: Novedades, DetallesPedido, Pedidos, Solicitudes, Notificaciones
            _context.NovedadesPedido.RemoveRange(_context.NovedadesPedido);
            _context.DetallesPedido.RemoveRange(_context.DetallesPedido);
            _context.Pedidos.RemoveRange(_context.Pedidos);
            _context.Solicitudes.RemoveRange(_context.Solicitudes);
            _context.Notificaciones.RemoveRange(_context.Notificaciones);

            // Opcional: vaciar el LOG. El registro de esta limpieza se escribe después, así queda como primer registro.
            var logsEliminados = 0;
            if (limpiarLog)
            {
                logsEliminados = await _context.LogsSistema.CountAsync();
                _context.LogsSistema.RemoveRange(_context.LogsSistema);
            }

            // Reiniciar estado de empleados a Disponible
            var empleados = await _context.Empleados.ToListAsync();
            foreach (var e in empleados)
            {
                e.EstadoDisponibilidad = "Disponible";
            }

            await _context.SaveChangesAsync();

            await _auditService.LogAsync(userEmail, "Administrador", "Limpieza de Base de Datos", "Mantenimiento",
                "Se ejecutó una limpieza completa de datos operativos (pedidos, novedades y solicitudes)"
                + (limpiarLog ? $" y del LOG del sistema ({logsEliminados} registros eliminados)." : "."));

            TempData["SuccessMessage"] = limpiarLog
                ? "La base de datos operativa y el LOG del sistema fueron limpiados con éxito. Los usuarios y maestros se mantuvieron protegidos."
                : "La base de datos operativa ha sido limpiada con éxito para pruebas. Los usuarios y maestros se mantuvieron protegidos.";
            return RedirectToAction("Index", "Home");
        }
    }
}