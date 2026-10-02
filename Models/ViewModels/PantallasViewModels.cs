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
        // Los más recientes, hasta MaxRegistros
        public List<LogSistema> Registros { get; init; } = new();
        public int TotalFiltrado { get; init; }
        public int MaxRegistros { get; init; }

        public string? ModuloFiltro { get; init; }
        public string? UsuarioFiltro { get; init; }

        // yyyy-MM-dd, el formato del <input type="date">
        public string? FechaDesdeFiltro { get; init; }
        public string? FechaHastaFiltro { get; init; }
    }
}
