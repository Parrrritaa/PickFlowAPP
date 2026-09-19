namespace PickingApp.Services
{
    public interface IEmailService
    {
        Task EnviarCodigoVerificacionAsync(string destinatarioEmail, string nombreDestinatario, string codigo);
    }
}
