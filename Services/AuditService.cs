using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PickingApp.Data;
using PickingApp.Models;

namespace PickingApp.Services
{
    // Guarda en la tabla LogsSistema quién hizo qué, en qué módulo y desde qué IP (LOG del sistema, CU-38)
    public class AuditService : IAuditService
    {
        private readonly DbContextOptions<ApplicationDbContext> _options;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public AuditService(DbContextOptions<ApplicationDbContext> options, IHttpContextAccessor httpContextAccessor)
        {
            _options = options;
            _httpContextAccessor = httpContextAccessor;
        }

        // Registra una acción del usuario que tiene la sesión iniciada (correo, rol e IP salen de la petición)
        public Task RegistrarAsync(string accion, string modulo, string detalle)
        {
            var user = _httpContextAccessor.HttpContext?.User;
            return LogAsync(user?.FindFirstValue(ClaimTypes.Email) ?? string.Empty,
                user?.FindFirstValue(ClaimTypes.Role) ?? string.Empty, accion, modulo, detalle);
        }

        // El LOG usa su propio contexto: guardarlo nunca debe confirmar los cambios que el
        // controlador todavía tiene pendientes en el contexto de la petición (quedarían a medias).
        // Para acciones sin sesión iniciada (login, recuperación de contraseña), donde el usuario se indica a mano
        public async Task LogAsync(string usuario, string rol, string accion, string modulo, string detalle, string? ip = null)
        {
            // Se toma antes de cualquier await: la llamada puede no esperarse y terminar después de la petición
            ip ??= _httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
            try
            {
                await using var db = new ApplicationDbContext(_options);
                if (!await ParametrosSistema.LogHabilitadoAsync(db)) return;

                int? usuarioId = null;
                if (!string.IsNullOrWhiteSpace(usuario))
                {
                    usuarioId = await db.Usuarios
                        .Where(u => u.Correo == usuario)
                        .Select(u => (int?)u.Id)
                        .FirstOrDefaultAsync();
                }

                var log = new LogSistema
                {
                    FechaHora = DateTime.Now,
                    Usuario = string.IsNullOrWhiteSpace(usuario) ? "Sistema" : usuario,
                    UsuarioId = usuarioId,
                    Rol = string.IsNullOrWhiteSpace(rol) ? "Sistema" : rol,
                    Accion = accion,
                    Modulo = modulo,
                    Detalle = detalle,
                    DireccionIp = ip
                };

                // Add prepara el INSERT y SaveChangesAsync lo ejecuta en MySQL
                db.LogsSistema.Add(log);
                await db.SaveChangesAsync();
            }
            catch
            {
                // Fallo silencioso en auditoría para no romper el flujo principal si hay error transitorio
            }
        }
    }
}
