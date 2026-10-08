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
        byte[] GenerarReporteTiemposPorEmpleadoPdf(List<ReporteTiemposEmpleadoItem> datos, PeriodoReporte periodo);
        byte[] GenerarReporteGeneralTiemposPdf(List<ReporteGeneralTiemposItem> datos, PeriodoReporte periodo);

        // Reporte individual (un empleado o una bodega): resumen + detalle de cada pedido completado.
        // porEmpleado = true muestra la columna Bodega; false muestra la columna Empleado.
        byte[] GenerarReporteDetalleTiemposPdf(string titulo, string sujeto, List<Pedido> pedidos, PeriodoReporte periodo, bool porEmpleado);

        byte[] GenerarReportePedidosPorPeriodoPdf(List<Pedido> pedidos, PeriodoReporte periodo, string? filtro);
        byte[] GenerarReporteCompletadosCanceladosReasignadosPdf(List<Pedido> pedidos, PeriodoReporte periodo, string? filtro);
        byte[] GenerarReporteDashboardPdf(DashboardPdfDatos datos);
        byte[] GenerarReporteLogSistemaPdf(List<LogSistema> logs, string? modulo, string? usuario, DateTime fechaDesde, DateTime fechaHasta);
    }
}