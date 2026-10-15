namespace PickingApp.Services
{
    // Contrato del envío de correos; la implementación está en EmailService.cs
    public interface IEmailService
    {
        // esCambioContrasena: true al confirmar un cambio de contraseña (CU-42), false al recuperarla
        Task EnviarCodigoVerificacionAsync(string destinatarioEmail, string nombreDestinatario, string codigo, bool esCambioContrasena);
    }
}
