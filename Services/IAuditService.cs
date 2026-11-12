using PickingApp.Models;

namespace PickingApp.Services
{
    // Contrato del registro de auditoría; la implementación está en AuditService.cs
    public interface IAuditService
    {
        Task RegistrarAsync(string accion, string modulo, string detalle);
        Task LogAsync(string usuario, string rol, string accion, string modulo, string detalle, string? ip = null);
    }
}
