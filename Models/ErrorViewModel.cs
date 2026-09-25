namespace PickingApp.Models
{
    public class ErrorViewModel
    {
        public string? RequestId { get; set; }

        public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);
    }

    public class ErrorBaseDatosViewModel
    {
        public bool SinConexion { get; set; }

        public string UrlReintento { get; set; } = "/";
    }
}
