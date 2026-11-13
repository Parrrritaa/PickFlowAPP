using System.ComponentModel.DataAnnotations;
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

    // Views/Empleados/Crear.cshtml y Editar.cshtml: solo lo que envía el formulario. El Id llega por la ruta, y fechas y
    // horarios son anulables para que un campo que no llega dé error en vez de guardarse como 00:00 o 01/01/0001.
    public class EmpleadoCampos
    {
        [Required(ErrorMessage = "La identificación es obligatoria")]
        [StringLength(30)]
        [RegularExpression(@"^\d+$", ErrorMessage = "La identificación solo debe contener números")]
        public string Identificacion { get; set; } = string.Empty;

        [Required(ErrorMessage = "El nombre completo es obligatorio")]
        [StringLength(100)]
        public string NombreCompleto { get; set; } = string.Empty;

        [EmailAddress]
        [StringLength(100)]
        public string? Correo { get; set; }

        [Required(ErrorMessage = "La hora de entrada es obligatoria")]
        public TimeSpan? HorarioEntrada { get; set; }

        [Required(ErrorMessage = "La hora de salida es obligatoria")]
        public TimeSpan? HorarioSalida { get; set; }

        [Required(ErrorMessage = "La fecha de ingreso es obligatoria")]
        public DateTime? FechaIngreso { get; set; }

        public int? BodegaId { get; set; }

        public static EmpleadoCampos De(Empleado empleado) => new()
        {
            Identificacion = empleado.Identificacion, NombreCompleto = empleado.NombreCompleto, Correo = empleado.Correo,
            HorarioEntrada = empleado.HorarioEntrada, HorarioSalida = empleado.HorarioSalida,
            FechaIngreso = empleado.FechaIngreso, BodegaId = empleado.BodegaId,
        };

        // Solo después de validar: los campos obligatorios ya tienen valor
        public Empleado ComoEmpleado() => new()
        {
            Identificacion = Identificacion, NombreCompleto = NombreCompleto, Correo = Correo,
            HorarioEntrada = HorarioEntrada!.Value, HorarioSalida = HorarioSalida!.Value,
            FechaIngreso = FechaIngreso!.Value, BodegaId = BodegaId,
        };
    }

    // Views/Usuarios/Crear.cshtml: solo lo que envía el formulario (la contraseña llega aparte y se guarda como hash).
    // RolId es anulable: el Supervisor no lo envía y el controlador lo fija; si falta en el del Administrador,
    // la validación contra el catálogo de roles lo rechaza en vez de crear un auxiliar sin avisar.
    public class UsuarioCampos
    {
        [Required(ErrorMessage = "El nombre completo es obligatorio")]
        [StringLength(100)]
        public string NombreCompleto { get; set; } = string.Empty;

        [StringLength(30)]
        [RegularExpression(@"^\d*$", ErrorMessage = "La identificación solo debe contener números")]
        public string? Identificacion { get; set; }

        [Required(ErrorMessage = "El correo es obligatorio")]
        [EmailAddress(ErrorMessage = "Formato de correo electrónico inválido")]
        [StringLength(100)]
        public string Correo { get; set; } = string.Empty;

        public int? RolId { get; set; }

        public string Estado { get; set; } = string.Empty;

        public int? BodegaId { get; set; }

        public Usuario ComoUsuario() => new()
        {
            NombreCompleto = NombreCompleto, Identificacion = Identificacion, Correo = Correo,
            RolId = RolId ?? 0, Estado = Estado, BodegaId = BodegaId,
        };
    }

    // Views/Empleados/Index.cshtml
    public class EmpleadosIndexViewModel
    {
        public List<Empleado> Empleados { get; init; } = new();
        public List<TrasladoPersonal> Trasladados { get; init; } = new();

        // Ids de los empleados con un pedido Asignado o Reasignado
        public HashSet<int> EmpleadosOcupados { get; init; } = new();

        // Factor de rendimiento de cada empleado, por Id
        public Dictionary<int, RendimientoEmpleado> Rendimientos { get; init; } = new();

        // Pestañas por bodega (solo el Administrador; vacío para los demás): la elegida y los empleados de cada una
        public List<Bodega> Bodegas { get; init; } = new();
        public Dictionary<int, int> EmpleadosPorBodega { get; init; } = new();
        public int? FiltroBodegaId { get; init; }
        public int TotalEmpleados { get; init; }
    }

    // Views/Empleados/Historial.cshtml: tiempos reales contra estimados de los pedidos que completó un empleado
    public class EmpleadoHistorialViewModel
    {
        public Empleado Empleado { get; init; } = null!;
        public List<PickingApp.Services.PeriodoBodega> Periodos { get; init; } = new();

        // Si dejó la bodega de quien consulta: su salida. En esa bodega se muestra como trasladado.
        public TrasladoPersonal? SalidaDeEstaBodega { get; init; }
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
