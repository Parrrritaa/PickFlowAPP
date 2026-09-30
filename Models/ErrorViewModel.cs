namespace PickingApp.Models
{
    public class ErrorViewModel
    {
        public string? RequestId { get; set; }

        public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);
    }

    // Datos de la página de aviso de base de datos (Views/Home/ErrorBaseDatos.cshtml)
    public class ErrorBaseDatosViewModel
    {
        public bool SinConexion { get; set; }

        public string UrlReintento { get; set; } = "/";
    }
}
