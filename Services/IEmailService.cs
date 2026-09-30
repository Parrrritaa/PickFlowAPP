namespace PickingApp.Services
{
    // Contrato del envío de correos; la implementación está en EmailService.cs
    public interface IEmailService
    {
        Task EnviarCodigoVerificacionAsync(string destinatarioEmail, string nombreDestinatario, string codigo);
    }
}
