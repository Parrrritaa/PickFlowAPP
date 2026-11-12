using PickingApp.Models;

namespace PickingApp.Services
{
    // Filas de los reportes: ReportesController las calcula con consultas a MySQL y ReportService las dibuja en el PDF
    public class ReporteTiemposEmpleadoItem
    {
        public string Identificacion { get; set; } = string.Empty;
        public string Empleado { get; set; } = string.Empty;
        public int TotalPedidos { get; set; }
        public double PromedioMinutos { get; set; }
        public double TiempoMinimo { get; set; }
        public double TiempoMaximo { get; set; }

        // Comparación con el tiempo estándar: solo los pedidos que lo tienen (los muy antiguos pueden no tenerlo).
        // Se guardan sumas para poder sacar el total del reporte ponderado por pedidos.
        public int ConEstandar { get; set; }
        public int DentroDelEstandar { get; set; }
        public double MinutosRealesConEstandar { get; set; }
        public double MinutosEstandar { get; set; }

        public double? PorcentajeCumplimiento => ConEstandar == 0 ? null : 100.0 * DentroDelEstandar / ConEstandar;

        // Positivo: tarda más que el estándar; negativo: menos
        public double? DesviacionPorcentaje => MinutosEstandar <= 0 ? null : 100.0 * (MinutosRealesConEstandar - MinutosEstandar) / MinutosEstandar;

        // Requiere pedidos completados con tiempo real y el Empleado cargado. Dentro del estándar: real ≤ estándar.
        public static List<ReporteTiemposEmpleadoItem> De(IEnumerable<Pedido> completados) => completados
            .Where(p => p.Empleado != null && p.TiempoProcesamientoMinutos.HasValue)
            .GroupBy(p => p.EmpleadoId)
            .Select(g =>
            {
                var conEstandar = g.Where(p => p.TiempoEstandarMinutos > 0).ToList();
                return new ReporteTiemposEmpleadoItem
                {
                    Identificacion = g.First().Empleado!.Identificacion,
                    Empleado = g.First().Empleado!.NombreCompleto,
                    TotalPedidos = g.Count(),
                    PromedioMinutos = Math.Round(g.Average(p => p.TiempoProcesamientoMinutos!.Value), 1),
                    TiempoMinimo = Math.Round(g.Min(p => p.TiempoProcesamientoMinutos!.Value), 1),
                    TiempoMaximo = Math.Round(g.Max(p => p.TiempoProcesamientoMinutos!.Value), 1),
                    ConEstandar = conEstandar.Count,
                    DentroDelEstandar = conEstandar.Count(p => p.TiempoProcesamientoMinutos <= p.TiempoEstandarMinutos),
                    MinutosRealesConEstandar = conEstandar.Sum(p => p.TiempoProcesamientoMinutos!.Value),
                    MinutosEstandar = conEstandar.Sum(p => p.TiempoEstandarMinutos!.Value)
                };
            })
            .OrderBy(i => i.Empleado)
            .ToList();
    }

    public class ReporteGeneralTiemposItem
    {
        public string Bodega { get; set; } = string.Empty;
        public int TotalCompletados { get; set; }
        public double PromedioMinutos { get; set; }
        public double TiempoTotalMinutos { get; set; }
    }

    public class ReporteEstadosPedidosItem
    {
        public string Estado { get; set; } = string.Empty;
        public int Cantidad { get; set; }
        public double Porcentaje { get; set; }
    }

    public class DashboardCategoriaItem
    {
        public string Etiqueta { get; set; } = string.Empty;
        public int Valor { get; set; }
        public double Porcentaje { get; set; }
    }

    // Todo lo que muestra el PDF del dashboard: tarjetas, gráfica con sus filtros y pedidos recientes
    public class DashboardPdfDatos
    {
        public int Pendientes { get; set; }
        public int EnProceso { get; set; }
        public int Completados { get; set; }
        public int Cancelados { get; set; }
        public int Reasignados { get; set; }
        public int Total => Pendientes + EnProceso + Completados + Cancelados + Reasignados;

        public string TituloGrafica { get; set; } = string.Empty;
        public string TipoGrafica { get; set; } = string.Empty;
        public string Periodo { get; set; } = string.Empty;
        public string Filtros { get; set; } = string.Empty;
        public int TotalGrafica { get; set; }
        public List<DashboardCategoriaItem> Categorias { get; set; } = new();

        // PNG de la gráfica tal como la ve el usuario; null si no hay datos o la imagen recibida no era válida
        public byte[]? ImagenGrafica { get; set; }

        public List<Pedido> Recientes { get; set; } = new();
    }

    // Cuántos pedidos hay en cada estado; los reportes lo muestran en tarjetas arriba de la tabla.
    // Reasignados = pedidos que cambiaron de auxiliar al menos una vez: también cuentan en su estado actual.
    public class ResumenEstadosPedidos
    {
        public ResumenEstadosPedidos(IReadOnlyDictionary<string, int> porEstado, int reasignados)
        {
            int De(string estado) => porEstado.TryGetValue(estado, out var n) ? n : 0;
            Total = porEstado.Values.Sum();
            Pendientes = De(PedidoEstados.Pendiente);
            EnProceso = De(PedidoEstados.Asignado) + De(PedidoEstados.Reasignado);
            Completados = De(PedidoEstados.Completado);
            Cancelados = De(PedidoEstados.Cancelado);
            Reasignados = reasignados;
        }

        public static ResumenEstadosPedidos De(IEnumerable<Pedido> pedidos)
        {
            var lista = pedidos.ToList();
            return new(lista.GroupBy(p => p.Estado).ToDictionary(g => g.Key, g => g.Count()),
                lista.Count(p => p.MotivoReasignacion != null));
        }

        public int Total { get; }
        public int Pendientes { get; }
        public int EnProceso { get; }
        public int Completados { get; }
        public int Cancelados { get; }
        public int Reasignados { get; }
    }

    // Periodo de un reporte. Sin fechas = todo el historial; con solo "Desde" o solo "Hasta" queda abierto por un lado.
    public class PeriodoReporte
    {
        public PeriodoReporte(DateTime? desde, DateTime? hasta)
        {
            Desde = desde?.Date;
            Hasta = hasta?.Date;
        }

        public DateTime? Desde { get; }
        public DateTime? Hasta { get; }

        // Límite superior exclusivo para incluir el día "Hasta" completo
        public DateTime? HastaExclusivo => Hasta?.AddDays(1);

        public bool EsValido => Desde == null || Hasta == null || Desde <= Hasta;

        public string Descripcion => (Desde, Hasta) switch
        {
            (null, null) => "Todo el historial",
            (DateTime d, null) => $"Desde el {d:dd/MM/yyyy}",
            (null, DateTime h) => $"Hasta el {h:dd/MM/yyyy}",
            (DateTime d, DateTime h) when d == h => $"{d:dd/MM/yyyy}",
            (DateTime d, DateTime h) => $"{d:dd/MM/yyyy} – {h:dd/MM/yyyy}"
        };

        public string ParaArchivo => (Desde, Hasta) switch
        {
            (null, null) => "Historial",
            (DateTime d, null) => $"desde_{d:yyyyMMdd}",
            (null, DateTime h) => $"hasta_{h:yyyyMMdd}",
            (DateTime d, DateTime h) => $"{d:yyyyMMdd}_{h:yyyyMMdd}"
        };
    }

    // Contrato de generación de PDFs; la implementación está en ReportService.cs
    public interface IReportService
    {
        // estados: los pedidos cargados en el periodo, por estado (los reportes de tiempos solo listan completados)
        byte[] GenerarReporteTiemposPorEmpleadoPdf(List<ReporteTiemposEmpleadoItem> datos, PeriodoReporte periodo, ResumenEstadosPedidos estados);
        byte[] GenerarReporteGeneralTiemposPdf(List<ReporteGeneralTiemposItem> datos, PeriodoReporte periodo, ResumenEstadosPedidos estados);

        // Reporte individual (un empleado o una bodega): resumen + detalle de cada pedido completado.
        // porEmpleado = true muestra la columna Bodega; false muestra la columna Empleado.
        byte[] GenerarReporteDetalleTiemposPdf(string titulo, string sujeto, List<Pedido> pedidos, PeriodoReporte periodo, bool porEmpleado,
            ResumenEstadosPedidos estados);

        byte[] GenerarReportePedidosPorPeriodoPdf(List<Pedido> pedidos, PeriodoReporte periodo, string? filtro);
        byte[] GenerarReporteCompletadosCanceladosReasignadosPdf(List<Pedido> pedidos, PeriodoReporte periodo, string? filtro);
        byte[] GenerarReporteDashboardPdf(DashboardPdfDatos datos);

        // Reportes de análisis: los datos se arman en ReportesAnalisis.cs
        byte[] GenerarReporteNovedadesPdf(DatosNovedades datos, PeriodoReporte periodo);
        byte[] GenerarReporteSolicitudesPdf(DatosSolicitudes datos, PeriodoReporte periodo);
        byte[] GenerarReporteProductosUbicacionesPdf(DatosProductosUbicaciones datos, PeriodoReporte periodo, string? filtro);
        byte[] GenerarReporteCargaHorariaPdf(DatosCargaHoraria datos, PeriodoReporte periodo, string? filtro);
        byte[] GenerarReporteLogSistemaPdf(List<LogSistema> logs, string? modulo, string? usuario, DateTime fechaDesde, DateTime fechaHasta);
    }
}