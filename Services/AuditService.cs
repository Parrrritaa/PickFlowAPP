using Microsoft.EntityFrameworkCore;
using PickingApp.Data;
using PickingApp.Models;

namespace PickingApp.Services
{
    public class AuditService : IAuditService
    {
        private readonly ApplicationDbContext _context;
        private readonly DbContextOptions<ApplicationDbContext> _options;

        public AuditService(ApplicationDbContext context, DbContextOptions<ApplicationDbContext> options)
        {
            _context = context;
            _options = options;
        }

        public Task<bool> IsLogEnabledAsync() => LogHabilitadoAsync(_context);

        private static async Task<bool> LogHabilitadoAsync(ApplicationDbContext context)
        {
            var config = await context.ConfiguracionesSistema
                .FirstOrDefaultAsync(c => c.Clave == "LogHabilitado");
            
            return config == null || config.Valor.Equals("true", StringComparison.OrdinalIgnoreCase);
        }

        public async Task SetLogEnabledAsync(bool enabled)
        {
            var config = await _context.ConfiguracionesSistema
                .FirstOrDefaultAsync(c => c.Clave == "LogHabilitado");

            if (config == null)
            {
                _context.ConfiguracionesSistema.Add(new ConfiguracionSistema
                {
                    Clave = "LogHabilitado",
                    Valor = enabled ? "true" : "false"
                });
            }
            else
            {
                config.Valor = enabled ? "true" : "false";
            }

            await _context.SaveChangesAsync();
        }

        // El LOG usa su propio contexto: guardarlo nunca debe confirmar los cambios que el
        // controlador todavía tiene pendientes en el contexto de la petición (quedarían a medias).
        public async Task LogAsync(string usuario, string rol, string accion, string modulo, string detalle, string? ip = null)
        {
            try
            {
                await using var db = new ApplicationDbContext(_options);
                if (!await LogHabilitadoAsync(db)) return;

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
