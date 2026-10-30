using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using PickingApp.Models;
using PickingApp.Services;

namespace PickingApp.Tests
{
    // Cada reporte se genera sin errores, con datos y vacío (ReportService)
    public class ReportesPdfTests
    {
        // Entorno sin wwwroot: los PDF salen sin logo
        private sealed class Entorno : IWebHostEnvironment
        {
            public string WebRootPath { get; set; } = Path.GetTempPath();
            public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
            public string ApplicationName { get; set; } = "PickingApp";
            public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
            public string ContentRootPath { get; set; } = Path.GetTempPath();
            public string EnvironmentName { get; set; } = "Development";
        }

        private static readonly ReportService Servicio = new(new Entorno(), NullLogger<ReportService>.Instance);
        private static readonly PeriodoReporte Periodo = new(new DateTime(2026, 10, 1), new DateTime(2026, 10, 8));

        private static void EsPdf(byte[] bytes) => Assert.Equal("%PDF", Encoding.ASCII.GetString(bytes, 0, 4));

        private static List<Pedido> Pedidos() => new()
        {
            new Pedido
            {
                CodigoPedido = "PED-1", Estado = PedidoEstados.Completado, Prioridad = Prioridades.Alta,
                FechaFinalizacion = new DateTime(2026, 10, 2), TiempoProcesamientoMinutos = 12.5,
                Bodega = new Bodega { Codigo = "BOD-01", Nombre = "Central" },
                Detalles = { new DetallePedido { Cantidad = 3, UbicacionId = 1 } }
            },
            new Pedido { CodigoPedido = "PED-2", Estado = PedidoEstados.Cancelado, MotivoCancelacion = "Cliente desistió" }
        };

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void TodosLosReportesGeneranUnPdf(bool conDatos)
        {
            var pedidos = conDatos ? Pedidos() : new List<Pedido>();
            var estados = ResumenEstadosPedidos.De(pedidos);

            EsPdf(Servicio.GenerarReporteTiemposPorEmpleadoPdf(conDatos
                ? new() { new ReporteTiemposEmpleadoItem { Identificacion = "1", Empleado = "Ana", TotalPedidos = 2, PromedioMinutos = 10 } }
                : new(), Periodo, estados));
            EsPdf(Servicio.GenerarReporteGeneralTiemposPdf(conDatos
                ? new() { new ReporteGeneralTiemposItem { Bodega = "Central", TotalCompletados = 2 } }
                : new(), Periodo, estados));
            EsPdf(Servicio.GenerarReportePedidosPorPeriodoPdf(pedidos, Periodo, null));
            EsPdf(Servicio.GenerarReporteCompletadosCanceladosReasignadosPdf(pedidos, Periodo, null));
            EsPdf(Servicio.GenerarReporteDetalleTiemposPdf("Detalle", "Empleado: Ana", pedidos, Periodo, porEmpleado: true, estados));
            EsPdf(Servicio.GenerarReporteLogSistemaPdf(conDatos
                ? new() { new LogSistema { FechaHora = DateTime.Now, Usuario = "a@b.com", Accion = "Prueba", Modulo = "Pedidos", Detalle = "detalle" } }
                : new(), null, null, Periodo.Desde!.Value, Periodo.Hasta!.Value));
            EsPdf(Servicio.GenerarReporteDashboardPdf(new DashboardPdfDatos { Periodo = "Hoy", Filtros = "Sin filtros", Recientes = pedidos }));
        }

        [Fact]
        public void ResumenCuentaCadaEstadoYLosReasignadosAparte()
        {
            var resumen = ResumenEstadosPedidos.De(new List<Pedido>
            {
                new() { Estado = PedidoEstados.Pendiente },
                new() { Estado = PedidoEstados.Asignado },
                new() { Estado = PedidoEstados.Reasignado, MotivoReasignacion = "Ausencia" },
                new() { Estado = PedidoEstados.Completado, MotivoReasignacion = "Cambio de turno" },
                new() { Estado = PedidoEstados.Completado },
                new() { Estado = PedidoEstados.Cancelado }
            });

            Assert.Equal(6, resumen.Total);
            Assert.Equal(1, resumen.Pendientes);
            Assert.Equal(2, resumen.EnProceso); // Asignado + Reasignado
            Assert.Equal(2, resumen.Completados);
            Assert.Equal(1, resumen.Cancelados);
            Assert.Equal(2, resumen.Reasignados); // también el que ya se completó
        }
    }
}
