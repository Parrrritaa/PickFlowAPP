using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickingApp.Data;
using PickingApp.Models;
using PickingApp.Services;

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
            var userId = User.IdUsuario();

            // Solo las pendientes: al marcarse como leída, la notificación se descarta de la lista
            var notificaciones = await _context.Notificaciones
                .Where(n => n.UsuarioDestinoId == userId && !n.Leida)
                .OrderByDescending(n => n.FechaCreacion)
                .Take(50)
                .ToListAsync();

            return View(notificaciones);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarcarLeida(int id, [FromHeader(Name = "X-Requested-With")] string? origen = null)
        {
            // Solo las propias: el Id de otra persona no encuentra nada (evita IDOR)
            var userId = User.IdUsuario();
            var notificacion = await _context.Notificaciones.FirstOrDefaultAsync(n => n.Id == id && n.UsuarioDestinoId == userId);
            var esDelUsuario = notificacion != null;

            if (notificacion != null)
            {
                notificacion.Leida = true;
                await _context.SaveChangesAsync();
            }

            // Petición desde la página (sin recarga): responde con el resultado para animar el descarte
            if (origen == "fetch")
            {
                return esDelUsuario ? NoContent() : NotFound();
            }

            return RedirectToAction(nameof(Index));
        }
    }
}