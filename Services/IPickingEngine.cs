using PickingApp.Models;

namespace PickingApp.Services
{
    public class AsignacionValidationResult
    {
        public bool PuedeAsignar { get; set; }
        public string? MensajeError { get; set; }
        public string? MotivoCorto { get; set; } // Etiqueta breve para mostrar en la lista de asignación
        public bool GeneraAdvertencia { get; set; }
        public string? MensajeAdvertencia { get; set; }
        public double TiempoEstimadoMinutos { get; set; }
        public double TiempoEstandarMinutos { get; set; }
    }

    public interface IPickingEngine
    {
        Task<AsignacionValidationResult> ValidarAsignacionAsync(int pedidoId, int empleadoId);
        Task<double> CalcularTiempoEstandarAsync(int pedidoId);
        Task<double> CalcularTiempoEstimadoAsync(int pedidoId, int empleadoId);
        Task<double> ObtenerMinutosPredeterminadosAsync();
        Task<double> ObtenerMinutosPorUbicacionAsync();
        double CalcularTiempoProcesamiento(DateTime fechaAsignacion, DateTime fechaFinalizacion, int margenMinutos = 2);
    }
}
