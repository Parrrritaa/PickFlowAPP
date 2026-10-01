using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickingApp.Data;
using PickingApp.Models;

namespace PickingApp.ViewComponents
{
    // Indicador de notificaciones/solicitudes sin atender en el menú lateral.
    // Reutiliza los mismos criterios de "sin atender" que NotificacionesController.Index
    // y SolicitudesController.Index, para que el número coincida con lo que se ve al entrar.
    public class MenuBadgesViewComponent : ViewComponent
    {
        private const int TipoGestionableSupervisor = TiposSolicitud.RegistroEmpleado;
        private readonly ApplicationDbContext _context;

        public MenuBadgesViewComponent(ApplicationDbContext context)
        {
            _context = context;
        }

        // _Layout.cshtml lo llama con Component.InvokeAsync("MenuBadges", ...) y muestra el número que devuelve
        public async Task<IViewComponentResult> InvokeAsync(string tipo)
        {
            var userIdClaim = UserClaimsPrincipal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userIdClaim == null || !int.TryParse(userIdClaim, out var userId))
            {
                return Content(string.Empty);
            }

            var userRole = UserClaimsPrincipal.FindFirstValue(ClaimTypes.Role);

            var count = tipo switch
            {
                "notificaciones" => await _context.Notificaciones.CountAsync(n => !n.Leida &&
                    (n.UsuarioDestinoId == userId || n.RolDestino!.Nombre == userRole ||
                     (n.UsuarioDestinoId == null && n.RolDestinoId == null))),

                "solicitudes" when userRole == Roles.Administrador =>
                    await _context.Solicitudes.CountAsync(s => s.Estado == EstadosSolicitud.Pendiente),

                "solicitudes" when userRole == Roles.Supervisor =>
                    await _context.Solicitudes.CountAsync(s => s.Estado == EstadosSolicitud.Pendiente && s.TipoSolicitudId == TipoGestionableSupervisor),

                _ => 0
            };

            return View(count);
        }
    }
}
