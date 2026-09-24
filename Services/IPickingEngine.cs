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
        public string? MotivoAdvertencia { get; set; } // Condición breve para la notificación al Administrador
        public double TiempoEstimadoMinutos { get; set; }
        public double TiempoEstandarMinutos { get; set; }
        public RendimientoEmpleado? Rendimiento { get; set; }
    }

    // Qué tan rápido trabaja un empleado frente al tiempo estándar: Factor = Σ tiempo real / Σ tiempo estándar
    // de sus pedidos completados (desde el último reinicio). Con menos pedidos que el mínimo se usa 1 (el estándar).
    public class RendimientoEmpleado
    {
        public const double FactorMinimo = 0.5;
        public const double FactorMaximo = 2.0;

        public double Factor { get; set; } = 1.0;       // El que se aplica a las estimaciones (limitado)
        public double? FactorMedido { get; set; }       // El observado, sin limitar; null si no hay pedidos
        public int PedidosConsiderados { get; set; }
        public int MinimoPedidos { get; set; }

        public bool TieneHistorialSuficiente => PedidosConsiderados >= MinimoPedidos;

        public double Estimar(double tiempoEstandar) => Math.Round(Math.Max(1.0, tiempoEstandar * Factor), 1);

        // Texto corto para tablas, p. ej. "Tarda 15 % menos"
        public string Descripcion
        {
            get
            {
                if (!TieneHistorialSuficiente) return "Sin historial suficiente";
                var porcentaje = (int)Math.Round(Math.Abs(1 - Factor) * 100);
                if (porcentaje < 5) return "Al ritmo estándar";
                return Factor < 1 ? $"Tarda {porcentaje} % menos" : $"Tarda {porcentaje} % más";
            }
        }

        public string Detalle => TieneHistorialSuficiente
            ? $"×{Factor:0.00} · {PedidosConsiderados} pedido(s)"
            : $"{PedidosConsiderados} de {MinimoPedidos} pedidos · usa el estándar";
    }

    public interface IPickingEngine
    {
        Task<AsignacionValidationResult> ValidarAsignacionAsync(int pedidoId, int empleadoId);
        Task<Dictionary<int, AsignacionValidationResult>> ValidarAsignacionesAsync(int pedidoId, IReadOnlyCollection<int> empleadoIds);
        Task<double> CalcularTiempoEstandarAsync(int pedidoId);
        Task<Dictionary<int, double>> CalcularTiemposEstandarAsync(IReadOnlyCollection<int> pedidoIds);
        Task<Dictionary<int, double>> CalcularMinutosPorLineaAsync(int pedidoId);
        Task<Dictionary<int, RendimientoEmpleado>> CalcularRendimientosAsync(IReadOnlyCollection<int> empleadoIds);
        Task<double> ObtenerMinutosPorUbicacionAsync();
        double CalcularTiempoProcesamiento(DateTime inicioProceso, DateTime fechaFinalizacion);
    }
}
