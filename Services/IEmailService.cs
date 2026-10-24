namespace PickingApp.Services
{
    // Contrato del envío de correos; la implementación está en EmailService.cs
    public interface IEmailService
    {
        Task EnviarCodigoVerificacionAsync(string destinatarioEmail, string nombreDestinatario, string codigo, PropositoCodigo proposito);
    }

    // Para qué es el código: cambia el título y el texto del correo
    public enum PropositoCodigo
    {
        RecuperarContrasena,
        CambiarContrasena,   // CU-42
        VerificarCorreo      // primer inicio de sesión de un usuario nuevo
    }
}
