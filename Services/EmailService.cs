using System.Globalization;
using System.Net;
using System.Net.Mail;
using System.Net.Mime;

namespace PickingApp.Services
{
    // Envía por SMTP los códigos de verificación de 6 dígitos (recuperar y cambiar contraseña)
    public class EmailService : IEmailService
    {
        private const string LogoContentId = "logo-pickflow";

        private readonly IConfiguration _configuration;
        private readonly IWebHostEnvironment _env;

        public EmailService(IConfiguration configuration, IWebHostEnvironment env)
        {
            _configuration = configuration;
            _env = env;
        }

        public async Task EnviarCodigoVerificacionAsync(string destinatarioEmail, string nombreDestinatario, string codigo, bool esCambioContrasena)
        {
            // Servidor, puerto y cuenta remitente se leen de appsettings.json → EmailSettings
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

            var titulo = esCambioContrasena ? "Confirma el cambio de contraseña" : "Recupera tu contraseña";
            var intro = esCambioContrasena
                ? "Pediste cambiar la contraseña de tu cuenta. Para aplicar la nueva, confirma con este código."
                : "Recibimos una solicitud para restablecer la contraseña de tu cuenta. Usa este código para crear una nueva.";

            var plantilla = await File.ReadAllTextAsync(Path.Combine(_env.ContentRootPath, "Plantillas", "CorreoCodigoVerificacion.html"));
            var html = ConstruirHtml(plantilla, titulo, intro, nombreDestinatario, codigo, destinatarioEmail, DateTime.Now);

            // EnableSsl cifra la conexión con el servidor de correo (puerto 587 con STARTTLS)
            using var client = new SmtpClient(host, port)
            {
                Credentials = new NetworkCredential(senderEmail, senderPassword),
                EnableSsl = true
            };

            // El texto plano queda como respaldo para clientes que no muestran HTML
            using var mensaje = new MailMessage
            {
                From = new MailAddress(senderEmail, senderName),
                Subject = $"{titulo} - PickFlow",
                Body = $"Hola {nombreDestinatario},\n\n" +
                       $"{intro}\n\n" +
                       $"Tu código de verificación es: {codigo}\n\n" +
                       "Este código es válido por 10 minutos. Si no solicitaste este código, ignora este mensaje.\n\n" +
                       "Este correo es automático. No lo respondas.",
                IsBodyHtml = false
            };
            mensaje.To.Add(destinatarioEmail);

            // El logo va embebido (cid:) porque Gmail bloquea imágenes en data: y rutas locales
            var vistaHtml = AlternateView.CreateAlternateViewFromString(html, System.Text.Encoding.UTF8, MediaTypeNames.Text.Html);
            var logo = new LinkedResource(Path.Combine(_env.WebRootPath, "images", "pickflow-logo.jpg"), MediaTypeNames.Image.Jpeg)
            {
                ContentId = LogoContentId
            };
            vistaHtml.LinkedResources.Add(logo);
            mensaje.AlternateViews.Add(vistaHtml);

            await client.SendMailAsync(mensaje);
        }

        // Rellena los marcadores {{...}} de la plantilla; todo dato se codifica para que un nombre con HTML no se inyecte en el correo
        public static string ConstruirHtml(string plantilla, string titulo, string intro, string nombre, string codigo, string correo, DateTime fecha)
        {
            string C(string valor) => WebUtility.HtmlEncode(valor);

            return plantilla
                .Replace("{{TITULO}}", C(titulo))
                .Replace("{{INTRO}}", C(intro))
                .Replace("{{NOMBRE}}", C(nombre))
                .Replace("{{CODIGO}}", C(codigo))
                .Replace("{{CORREO}}", C(correo))
                .Replace("{{FECHA}}", C(fecha.ToString("dd/MM/yyyy h:mm tt", new CultureInfo("es-CO"))))
                .Replace("{{ANIO}}", fecha.Year.ToString(CultureInfo.InvariantCulture));
        }
    }
}
