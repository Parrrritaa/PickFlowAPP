using System.Net;
using System.Net.Mail;

namespace PickingApp.Services
{
    public class EmailService : IEmailService
    {
        private readonly IConfiguration _configuration;

        public EmailService(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public async Task EnviarCodigoVerificacionAsync(string destinatarioEmail, string nombreDestinatario, string codigo)
        {
            var host = _configuration["EmailSettings:SmtpHost"];
            var portConfig = _configuration["EmailSettings:SmtpPort"];
            var senderEmail = _configuration["EmailSettings:SenderEmail"];
            var senderPassword = _configuration["EmailSettings:SenderPassword"];
            var senderName = _configuration["EmailSettings:SenderName"] ?? "PickFlow";

            if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(senderEmail) || string.IsNullOrWhiteSpace(senderPassword))
            {
                throw new InvalidOperationException("La configuración de correo (EmailSettings) no está definida.");
            }

            var port = int.Parse(portConfig ?? "587");

            using var client = new SmtpClient(host, port)
            {
                Credentials = new NetworkCredential(senderEmail, senderPassword),
                EnableSsl = true
            };

            using var mensaje = new MailMessage
            {
                From = new MailAddress(senderEmail, senderName),
                Subject = "Código de verificación - PickFlow",
                Body = $"Hola {nombreDestinatario},\n\n" +
                       $"Tu código de verificación es: {codigo}\n\n" +
                       "Este código es válido por 10 minutos. Si no solicitaste este código, ignora este mensaje.",
                IsBodyHtml = false
            };
            mensaje.To.Add(destinatarioEmail);

            await client.SendMailAsync(mensaje);
        }
    }
}
