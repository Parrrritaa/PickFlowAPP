using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickingApp.Data;
using PickingApp.Models;
using PickingApp.Models.ViewModels;
using PickingApp.Services;

namespace PickingApp.Controllers
{
    // Catálogo de observaciones para responder solicitudes (Sistema > Observaciones). Solo el Administrador.
    // Se agregan y se activan o desactivan; no se editan ni se borran para no cambiar el texto de solicitudes ya respondidas.
    [Authorize(Roles = Roles.Administrador)]
    public class ObservacionesController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IAuditService _auditService;

        public ObservacionesController(ApplicationDbContext context, IAuditService auditService)
        {
            _context = context;
            _auditService = auditService;
        }

        public async Task<IActionResult> Index(int? tipo)
        {
            var consulta = _context.ObservacionesSolicitud.AsQueryable();
            if (tipo.HasValue) consulta = consulta.Where(o => o.TipoSolicitudId == tipo);

            var observaciones = await consulta
                .OrderBy(o => o.TipoSolicitudId).ThenBy(o => o.Decision).ThenBy(o => o.Texto)
                .ToListAsync();

            // Cuántas solicitudes respondieron con cada una: ayuda a decidir cuál desactivar
            var usos = await _context.Solicitudes
                .Where(s => s.ObservacionId != null)
                .GroupBy(s => s.ObservacionId!.Value)
                .Select(g => new { g.Key, Total = g.Count() })
                .ToDictionaryAsync(g => g.Key, g => g.Total);

            return View(new ObservacionesIndexViewModel
            {
                Observaciones = observaciones,
                Usos = usos,
                TipoFiltro = tipo
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Crear(int tipoSolicitudId, string? decision, string? texto)
        {
            texto = (texto ?? string.Empty).Trim();
            var tipo = TiposSolicitud.Catalogo.FirstOrDefault(t => t.Id == tipoSolicitudId);

            string? error = null;
            if (tipo == null) error = "Seleccione un tipo de solicitud válido.";
            else if (decision == null || !DecisionesSolicitud.Todas.Contains(decision)) error = "Seleccione si la observación es para aprobar o para rechazar.";
            else if (texto.Length < 3 || texto.Length > 150) error = "La observación debe tener entre 3 y 150 caracteres.";
            // La comparación de MySQL ignora mayúsculas y tildes, igual que el índice único de la tabla
            else if (await _context.ObservacionesSolicitud.AnyAsync(o => o.TipoSolicitudId == tipoSolicitudId && o.Decision == decision && o.Texto == texto))
                error = "Esa observación ya existe para ese tipo de solicitud y decisión.";

            if (error != null)
            {
                TempData["ErrorMessage"] = error;
                return RedirectToAction(nameof(Index), new { tipo = tipoSolicitudId });
            }

            _context.ObservacionesSolicitud.Add(new ObservacionSolicitud { TipoSolicitudId = tipoSolicitudId, Decision = decision!, Texto = texto });
            await _context.SaveChangesAsync();

            await _auditService.RegistrarAsync(AccionesLog.CrearObservacion, "Solicitudes",
                $"Observación agregada para {tipo!.Nombre} ({decision}): {texto}");

            TempData["SuccessMessage"] = $"Observación \"{texto}\" agregada.";
            return RedirectToAction(nameof(Index), new { tipo = tipoSolicitudId });
        }

        // Activa o desactiva: una observación desactivada deja de aparecer al responder, pero las solicitudes
        // que ya la usan la siguen mostrando
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CambiarEstado(int id, int? tipo)
        {
            var observacion = await _context.ObservacionesSolicitud.FindAsync(id);
            if (observacion == null) return NotFound();

            observacion.Activa = !observacion.Activa;
            await _context.SaveChangesAsync();

            await _auditService.RegistrarAsync(observacion.Activa ? AccionesLog.ActivarObservacion : AccionesLog.DesactivarObservacion, "Solicitudes",
                $"Observación {(observacion.Activa ? "activada" : "desactivada")} para {observacion.NombreTipo} ({observacion.Decision}): {observacion.Texto}");

            TempData["SuccessMessage"] = $"Observación \"{observacion.Texto}\" {(observacion.Activa ? "activada" : "desactivada")}.";
            return RedirectToAction(nameof(Index), new { tipo });
        }
    }
}
