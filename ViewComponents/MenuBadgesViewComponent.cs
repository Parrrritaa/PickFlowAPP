using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickingApp.Data;

namespace PickingApp.ViewComponents
{
    // Indicador de notificaciones/solicitudes sin atender en el menú lateral.
    // Reutiliza los mismos criterios de "sin atender" que NotificacionesController.Index
    // y SolicitudesController.Index, para que el número coincida con lo que se ve al entrar.
    public class MenuBadgesViewComponent : ViewComponent
    {
        private const string TipoGestionableSupervisor = "RegistroEmpleado";
        private readonly ApplicationDbContext _context;

        public MenuBadgesViewComponent(ApplicationDbContext context)
        {
            _context = context;
        }

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
                    (n.UsuarioDestinoId == userId || n.RolDestino == userRole ||
                     (n.UsuarioDestinoId == null && n.RolDestino == null))),

                "solicitudes" when userRole == "Administrador" =>
                    await _context.Solicitudes.CountAsync(s => s.Estado == "Pendiente"),

                "solicitudes" when userRole == "Supervisor" =>
                    await _context.Solicitudes.CountAsync(s => s.Estado == "Pendiente" && s.TipoSolicitud == TipoGestionableSupervisor),

                _ => 0
            };

            return View(count);
        }
    }
}
