using System.Security.Claims;

namespace PickingApp.Services
{
    public static class ClaimsPrincipalExtensions
    {
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
