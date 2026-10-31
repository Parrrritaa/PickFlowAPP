using PickingApp.Services;

namespace PickingApp.Models.ViewModels
{
    // Views/Pedidos/Index.cshtml: lista de pedidos con los filtros aplicados y las opciones de cada filtro
    public class PedidosIndexViewModel
    {
        // Solo la página actual; el total con los filtros está en Paginacion.TotalRegistros
        public List<Pedido> Pedidos { get; init; } = new();
        public Paginacion Paginacion { get; init; } = null!;
        public List<Bodega> Bodegas { get; init; } = new();
        public List<Empleado> Empleados { get; init; } = new();

        public string? FiltroEstado { get; init; }

        // Pedidos de cada estado con los demás filtros y la búsqueda: el número de cada pestaña
        public Dictionary<string, int> ConteoPorEstado { get; init; } = new();
        public string? FiltroPrioridad { get; init; }
        public int? FiltroBodegaId { get; init; }
        public int? FiltroEmpleadoId { get; init; }
        public string? FiltroFechaDesde { get; init; }
        public string? FiltroFechaHasta { get; init; }
        public string? Busqueda { get; init; }

        // Franja de novedades: "pendientes", "todas" o un tipo (FiltrosNovedad / TiposNovedad)
        public string? FiltroNovedad { get; init; }
        public Dictionary<string, int> NovedadesSinAtenderPorTipo { get; init; } = new();
        public int PedidosConNovedadSinAtender { get; init; }
        public bool HayNovedadesAtendidas { get; init; }

        // true: pestaña de pedidos divididos con sus partes
        public bool VerDivididos { get; init; }
        public int DivididosEnCurso { get; init; }
    }

    // Views/Pedidos/Detalle.cshtml
    public class PedidoDetalleViewModel
    {
        public Pedido Pedido { get; init; } = null!;
        public double TiempoEstandar { get; init; }
        public double MaxHorasJornada { get; init; }
        public string CategoriaPredeterminada { get; init; } = "Predeterminada";

        // Tiempo estándar de cada parte, por Id de la parte
        public Dictionary<int, double> TiemposPartes { get; init; } = new();
    }

    // Views/Pedidos/Asignar.cshtml: asignar o, si ya tiene empleado en curso, reasignar (CU-26 / CU-27)
    public class AsignarPedidoViewModel
    {
        public Pedido Pedido { get; init; } = null!;

        // Empleados activos, primero los que pueden recibir el pedido
        public List<Empleado> Empleados { get; init; } = new();
        public Dictionary<int, AsignacionValidationResult> Validaciones { get; init; } = new();
        public double TiempoEstandar { get; init; }
        public int? EmpleadoRecomendadoId { get; init; }

        // Con valor, la vista reasigna en lugar de asignar
        public int? EmpleadoActualId { get; init; }
        public string? EmpleadoActualNombre { get; init; }
    }

    // Views/Pedidos/Dividir.cshtml
    public class DividirPedidoViewModel
    {
        public Pedido Pedido { get; init; } = null!;
        public double TiempoEstandar { get; init; }
        public double JornadaMinutos { get; init; }

        // Minutos de cada línea del pedido, por Id del detalle
        public Dictionary<int, double> MinutosPorLinea { get; init; } = new();
        public double MinutosPorUbicacion { get; init; }
        public int MaxPartes { get; init; }
        public int PartesSugeridas { get; init; }
    }
}
