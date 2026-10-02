using PickingApp.Services;

namespace PickingApp.Models.ViewModels
{
    // Views/Home/Index.cshtml (Dashboard): total de pedidos por estado y los últimos pedidos
    public class DashboardViewModel
    {
        public int TotalPendientes { get; init; }
        public int TotalEnProceso { get; init; }
        public int TotalCompletados { get; init; }
        public int TotalCancelados { get; init; }
        public int TotalReasignados { get; init; }

        // Opciones del filtro de bodega del gráfico
        public List<Bodega> Bodegas { get; init; } = new();
        public List<Pedido> UltimosPedidos { get; init; } = new();
    }

    // Views/Empleados/Index.cshtml
    public class EmpleadosIndexViewModel
    {
        public List<Empleado> Empleados { get; init; } = new();

        // Ids de los empleados con un pedido Asignado o Reasignado
        public HashSet<int> EmpleadosOcupados { get; init; } = new();

        // Factor de rendimiento de cada empleado, por Id
        public Dictionary<int, RendimientoEmpleado> Rendimientos { get; init; } = new();
    }

    // Views/Empleados/Historial.cshtml: tiempos reales contra estimados de los pedidos que completó un empleado
    public class EmpleadoHistorialViewModel
    {
        public Empleado Empleado { get; init; } = null!;
        public bool Ocupado { get; init; }
        public RendimientoEmpleado Rendimiento { get; init; } = null!;
        public TendenciaRendimiento Tendencia { get; init; } = null!;

        public int TotalCompletados { get; init; }
        // Los anteriores al último "Reiniciar rendimiento" se muestran pero no cuentan
        public int CompletadosQueCuentan { get; init; }

        // Σ real ÷ Σ estimado - 1 de los pedidos con estimación guardada: 0,1 = tardó 10 % más de lo estimado.
        // null si ningún pedido tiene estimación
        public double? DesvioSobreEstimado { get; init; }

        // Gráfica: los últimos pedidos, del más antiguo al más reciente
        public List<HistorialPedidoFila> Grafica { get; init; } = new();

        // Tabla: una página, del más reciente al más antiguo
        public List<HistorialPedidoFila> Pedidos { get; init; } = new();
        public Paginacion Paginacion { get; init; } = null!;
    }

    public class HistorialPedidoFila
    {
        public int PedidoId { get; init; }
        public string CodigoPedido { get; init; } = string.Empty;
        public DateTime Finalizado { get; init; }
        public double Estandar { get; init; }
        public double? Estimado { get; init; }
        public double Real { get; init; }
        public bool CuentaParaRendimiento { get; init; }

        // Real frente a estimado: 0,25 = tardó 25 % más; null sin estimación
        public double? Desvio => Estimado > 0 ? Real / Estimado.Value - 1 : null;
    }

    // Views/Productos/Index.cshtml: categorías arriba y tabla de productos abajo
    public class ProductosIndexViewModel
    {
        public List<Producto> Productos { get; init; } = new();
        public List<CategoriaProducto> Categorias { get; init; } = new();
        public double MinutosPorUbicacion { get; init; }
    }

    // Views/Reportes/Index.cshtml: opciones para los reportes individuales (incluye inactivos)
    public class ReportesIndexViewModel
    {
        public List<Empleado> Empleados { get; init; } = new();
        public List<Bodega> Bodegas { get; init; } = new();
    }

    // Views/Auditoria/Index.cshtml (LOG del sistema)
    public class AuditoriaIndexViewModel
    {
        // Solo la página actual, del más reciente al más antiguo; el total con los filtros está en Paginacion.TotalRegistros
        public List<LogSistema> Registros { get; init; } = new();
        public Paginacion Paginacion { get; init; } = null!;

        public string? ModuloFiltro { get; init; }
        public string? UsuarioFiltro { get; init; }

        // yyyy-MM-dd, el formato del <input type="date">
        public string? FechaDesdeFiltro { get; init; }
        public string? FechaHastaFiltro { get; init; }
    }
}
