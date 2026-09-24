using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickingApp.Data;
using PickingApp.Models;
using PickingApp.Models.ViewModels;
using PickingApp.Services;

namespace PickingApp.Controllers
{
    [Authorize(Roles = Roles.Administrador)]
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

            await _auditService.RegistrarAsync("Exportar LOG a PDF", "Auditoría",
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
            return View(new ConfiguracionViewModel
            {
                LogHabilitado = await ParametrosSistema.LogHabilitadoAsync(_context),
                Valores = await ParametrosSistema.ObtenerTodosAsync(_context)
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GuardarConfiguracion(Dictionary<string, string> configuraciones)
        {
            // Los parámetros se aplican de inmediato: un valor fuera de rango (p. ej. 0 intentos) no se guarda.
            // Solo se aceptan las claves definidas en ParametrosSistema.
            var valores = new Dictionary<string, string>();
            var errores = new List<string>();
            foreach (var (clave, parametro) in ParametrosSistema.Numericos)
            {
                if (!configuraciones.TryGetValue(clave, out var texto)) continue;
                if (ParametrosSistema.EsValido(parametro, texto, out var valor))
                {
                    valores[clave] = Formato.ParaInput(valor);
                }
                else
                {
                    var tipo = parametro.Entero ? "un número entero" : "un número";
                    errores.Add($"{parametro.Nombre}: debe ser {tipo} entre {parametro.Minimo:0.##} y {parametro.Maximo:0.##}");
                }
            }
            if (configuraciones.TryGetValue(ParametrosSistema.LogHabilitado, out var log))
            {
                valores[ParametrosSistema.LogHabilitado] = log == "false" ? "false" : "true";
            }

            if (errores.Any())
            {
                TempData["ErrorMessage"] = "No se guardaron los cambios. " + string.Join(". ", errores) + ".";
                return RedirectToAction(nameof(Configuracion));
            }

            foreach (var kvp in valores)
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

            await _auditService.RegistrarAsync("Actualizar Configuración", "Sistema", 
                "Configuración general del sistema actualizada.");

            TempData["SuccessMessage"] = "Parámetros del sistema y auditoría guardados exitosamente.";
            return RedirectToAction(nameof(Configuracion));
        }

        // CU-40: Eliminar registros del LOG
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EliminarLogs(DateTime? hastaFecha)
        {
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

            await _auditService.RegistrarAsync("Depuración de LOGs", "Auditoría", 
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

            await _context.SaveChangesAsync();

            // Con las tablas ya vacías, se reinicia el autoincremento para que los próximos
            // registros vuelvan a empezar en 1 (evita IDs de pedidos con números muy altos).
            var tablasAReiniciar = new List<string> { "NovedadesPedido", "DetallesPedido", "Pedidos", "Solicitudes", "Notificaciones" };
            if (limpiarLog)
            {
                tablasAReiniciar.Add("LogsSistema");
            }
            foreach (var tabla in tablasAReiniciar)
            {
                // Los nombres de tabla vienen de la lista fija de arriba, no de entrada del usuario.
#pragma warning disable EF1002
                await _context.Database.ExecuteSqlRawAsync($"ALTER TABLE `{tabla}` AUTO_INCREMENT = 1");
#pragma warning restore EF1002
            }

            await _auditService.RegistrarAsync("Limpieza de Base de Datos", "Mantenimiento",
                "Se ejecutó una limpieza completa de datos operativos (pedidos, novedades y solicitudes)"
                + (limpiarLog ? $" y del LOG del sistema ({logsEliminados} registros eliminados)." : ".")
                + " Los autoincrementos de esas tablas se reiniciaron.");

            TempData["SuccessMessage"] = limpiarLog
                ? "La base de datos operativa y el LOG del sistema fueron limpiados con éxito. Los usuarios y maestros se mantuvieron protegidos."
                : "La base de datos operativa ha sido limpiada con éxito para pruebas. Los usuarios y maestros se mantuvieron protegidos.";
            return RedirectToAction("Index", "Home");
        }
    }
}