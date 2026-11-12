using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using PickingApp.Data;
using PickingApp.Middleware;
using PickingApp.Models;

namespace PickingApp.Services
{
    // Datos del usuario que viajan en la cookie de sesión y su comprobación contra la base de datos en cada petición.
    public static class SesionUsuario
    {
        private const string ClaimSelloSeguridad = "SelloSeguridad";
        public const string ClaimBodega = "BodegaId";

        public static ClaimsPrincipal CrearPrincipal(Usuario usuario)
        {
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, usuario.Id.ToString()),
                new(ClaimTypes.Name, usuario.NombreCompleto),
                new(ClaimTypes.Email, usuario.Correo),
                new(ClaimTypes.Role, usuario.NombreRol),
                new(ClaimSelloSeguridad, usuario.SelloSeguridad)
            };
            if (usuario.BodegaId != null) claims.Add(new(ClaimBodega, usuario.BodegaId.Value.ToString()));
            return new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
        }

        // Supervisor y Auxiliar solo trabajan dentro de una bodega activa; el Administrador no tiene bodega.
        // Requiere usuario.Bodega cargada. Null si puede entrar.
        public static string? MotivoSinBodega(Usuario usuario)
        {
            if (usuario.RolId == Roles.IdAdministrador) return null;
            if (usuario.Bodega == null) return "Tu cuenta todavía no tiene una bodega asignada. Pide al Administrador que te asigne una.";
            if (!usuario.Bodega.Activa) return $"La bodega {usuario.Bodega.Nombre} está inactiva. Pide al Administrador que te traslade a otra bodega.";
            return null;
        }

        // Cierra la sesión si la cuenta dejó de estar activa, se quedó sin bodega activa o cambió su contraseña, y reemite
        // la cookie si el Administrador le cambió el rol, el nombre o la bodega: los permisos siguen a la base de datos, no a la cookie.
        public static async Task ValidarAsync(CookieValidatePrincipalContext contexto)
        {
            var principal = contexto.Principal!;
            if (!int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id))
            {
                await RechazarAsync(contexto);
                return;
            }

            Usuario? usuario;
            try
            {
                var db = contexto.HttpContext.RequestServices.GetRequiredService<ApplicationDbContext>();
                usuario = await db.Usuarios.AsNoTracking().Include(u => u.Bodega).FirstOrDefaultAsync(u => u.Id == id);
            }
            catch (Exception ex) when (ErroresBaseDatos.EsErrorDeBaseDatos(ex))
            {
                // Sin base de datos la acción tampoco puede leer ni guardar nada y termina en el aviso de base de datos
                return;
            }

            if (usuario == null ||
                usuario.Estado != EstadosUsuario.Activo ||
                MotivoSinBodega(usuario) != null ||
                usuario.SelloSeguridad != principal.FindFirstValue(ClaimSelloSeguridad))
            {
                await RechazarAsync(contexto);
                return;
            }

            var actualizado = CrearPrincipal(usuario);
            if (actualizado.Claims.Any(c => principal.FindFirstValue(c.Type) != c.Value))
            {
                contexto.ReplacePrincipal(actualizado);
                contexto.ShouldRenew = true;
            }
        }

        private static async Task RechazarAsync(CookieValidatePrincipalContext contexto)
        {
            contexto.RejectPrincipal();
            await contexto.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }
    }
}
