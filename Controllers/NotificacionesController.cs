using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickingApp.Data;
using PickingApp.Models;

namespace PickingApp.Controllers
{
    [Authorize]
    public class NotificacionesController : Controller
    {
        private readonly ApplicationDbContext _context;

        public NotificacionesController(ApplicationDbContext context)
        {
            _context = context;
        }

        // CU-43: Mostrar notificaciones internas
        public async Task<IActionResult> Index()
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var userRole = User.FindFirstValue(ClaimTypes.Role) ?? Roles.Auxiliar;

            // Solo las pendientes: al marcarse como leída, la notificación se descarta de la lista
            var notificaciones = await _context.Notificaciones
                .Where(n => !n.Leida)
                .Where(n => n.UsuarioDestinoId == userId || n.RolDestino == userRole || (n.UsuarioDestinoId == null && n.RolDestino == null))
                .OrderByDescending(n => n.FechaCreacion)
                .Take(50)
                .ToListAsync();

            return View(notificaciones);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarcarLeida(int id)
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var userRole = User.FindFirstValue(ClaimTypes.Role) ?? Roles.Auxiliar;

            var notificacion = await _context.Notificaciones.FindAsync(id);

            // Evita que un usuario marque como leída una notificación dirigida específicamente a otro usuario (IDOR).
            // Las notificaciones por rol o globales sí son compartidas y cualquiera del rol puede marcarlas.
            var esDelUsuario = notificacion != null &&
                (notificacion.UsuarioDestinoId == null || notificacion.UsuarioDestinoId == userId) &&
                (notificacion.RolDestino == null || notificacion.RolDestino == userRole || notificacion.UsuarioDestinoId == userId);

            if (notificacion != null && esDelUsuario)
            {
                notificacion.Leida = true;
                await _context.SaveChangesAsync();
            }

            // Petición desde la página (sin recarga): responde con el resultado para animar el descarte
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return esDelUsuario ? NoContent() : NotFound();
            }

            return RedirectToAction(nameof(Index));
        }
    }
}