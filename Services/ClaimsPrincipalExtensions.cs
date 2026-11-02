using System.Security.Claims;

namespace PickingApp.Services
{
    public static class ClaimsPrincipalExtensions
    {
        // Id del usuario en sesión; solo para acciones con [Authorize], donde la cookie siempre lo trae.
        public static int IdUsuario(this ClaimsPrincipal user) => int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);

        // Bodega del Supervisor o Auxiliar en sesión; null para el Administrador, que ve todas.
        public static int? IdBodega(this ClaimsPrincipal user) =>
            int.TryParse(user.FindFirstValue(SesionUsuario.ClaimBodega), out var id) ? id : null;

        // Texto para identificar al usuario en notificaciones sin exponer su correo, p. ej. "administrador Jonathan Romero".
        public static string DescripcionParaNotificacion(this ClaimsPrincipal user)
        {
            var nombre = user.FindFirstValue(ClaimTypes.Name);
            var rol = user.FindFirstValue(ClaimTypes.Role);
            return DescribirUsuario(nombre, rol);
        }

        public static string DescribirUsuario(string? nombre, string? rol)
        {
            nombre = string.IsNullOrWhiteSpace(nombre) ? "Usuario" : nombre.Trim();
            return string.IsNullOrWhiteSpace(rol) ? nombre : $"{rol.Trim().ToLowerInvariant()} {nombre}";
        }
    }
}
