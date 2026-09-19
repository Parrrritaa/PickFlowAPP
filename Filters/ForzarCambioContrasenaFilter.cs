using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using PickingApp.Data;

namespace PickingApp.Filters
{
    // RN-06-CU041: si el usuario tiene una contraseña temporal pendiente de reemplazo (tras una
    // recuperación aprobada), se le bloquea el acceso a cualquier otra pantalla hasta que la cambie.
    public class ForzarCambioContrasenaFilter : IAsyncActionFilter
    {
        private readonly ApplicationDbContext _context;

        public ForzarCambioContrasenaFilter(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var controllerName = context.RouteData.Values["controller"]?.ToString();
            var user = context.HttpContext.User;

            if (controllerName == "Account" || user.Identity == null || !user.Identity.IsAuthenticated)
            {
                await next();
                return;
            }

            var userIdString = user.FindFirstValue(ClaimTypes.NameIdentifier);
            if (int.TryParse(userIdString, out int userId))
            {
                var debeCambiar = await _context.Usuarios
                    .Where(u => u.Id == userId)
                    .Select(u => u.DebeCambiarContrasena)
                    .FirstOrDefaultAsync();

                if (debeCambiar)
                {
                    context.Result = new RedirectToActionResult("CambiarContrasena", "Account", null);
                    return;
                }
            }

            await next();
        }
    }
}
