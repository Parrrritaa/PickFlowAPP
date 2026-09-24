using PickingApp.Models;

namespace PickingApp.Services
{
    public interface IAuditService
    {
        Task RegistrarAsync(string accion, string modulo, string detalle);
        Task LogAsync(string usuario, string rol, string accion, string modulo, string detalle, string? ip = null);
        Task<bool> IsLogEnabledAsync();
    }
}
