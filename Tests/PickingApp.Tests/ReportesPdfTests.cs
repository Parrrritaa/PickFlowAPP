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

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void LosReportesDeAnalisisGeneranUnPdf(bool conDatos)
        {
            var ana = new Empleado { Identificacion = "1", NombreCompleto = "Ana", HorarioEntrada = new(22, 0, 0), HorarioSalida = new(6, 0, 0) };
            var bodega = new Bodega { Codigo = "BOD-01", Nombre = "Central" };
            var pedido = new Pedido
            {
                Id = 1, CodigoPedido = "PED-1", Estado = PedidoEstados.Completado, Empleado = ana, Bodega = bodega,
                FechaCarga = new DateTime(2026, 10, 5, 9, 0, 0), FechaFinalizacion = new DateTime(2026, 10, 5, 9, 30, 0),
                TiempoProcesamientoMinutos = 30, TiempoEstandarMinutos = 25
            };
            var pedidos = conDatos ? new List<Pedido> { pedido } : new();
            var novedades = conDatos ? new List<NovedadPedido> { new() { Pedido = pedido, TipoNovedad = TiposNovedad.Faltante, Descripcion = "Faltó 1", RegistradoPor = "Ana" } } : new();
            var detalles = conDatos ? new List<DetallePedido> { new() { PedidoId = 1, CodigoProducto = "P1", NombreProducto = "Mouse", Cantidad = 2, UbicacionId = 1 } } : new();

            EsPdf(Servicio.GenerarReporteNovedadesPdf(DatosNovedades.De(novedades), Periodo));
            EsPdf(Servicio.GenerarReporteSolicitudesPdf(DatosSolicitudes.De(new List<Solicitud>()), Periodo));
            EsPdf(Servicio.GenerarReporteProductosUbicacionesPdf(DatosProductosUbicaciones.De(detalles), Periodo, null));
            EsPdf(Servicio.GenerarReporteCargaHorariaPdf(DatosCargaHoraria.De(pedidos, new[] { ana }), Periodo, null));
        }

        [Fact]
        public void TiemposPorEmpleadoCuentaLosPedidosDentroDelEstandar()
        {
            var ana = new Empleado { Identificacion = "1", NombreCompleto = "Ana" };
            Pedido Completado(double real, double? estandar) => new()
            {
                EmpleadoId = 1, Empleado = ana, Estado = PedidoEstados.Completado, TiempoProcesamientoMinutos = real, TiempoEstandarMinutos = estandar
            };

            // El último no tiene estándar: cuenta en los tiempos, no en el cumplimiento
            var fila = Assert.Single(ReporteTiemposEmpleadoItem.De(new[] { Completado(10, 12), Completado(12, 12), Completado(20, 12), Completado(6, null) }));

            Assert.Equal(4, fila.TotalPedidos);
            Assert.Equal(12, fila.PromedioMinutos);
            Assert.Equal(3, fila.ConEstandar);
            Assert.Equal(2, fila.DentroDelEstandar); // igual al estándar cuenta como dentro
            Assert.Equal(66.7, fila.PorcentajeCumplimiento!.Value, 1);
            Assert.Equal(16.7, fila.DesviacionPorcentaje!.Value, 1); // (42 - 36) / 36
        }

        [Fact]
        public void TiempoDeAtencionYDeRespuestaSoloCuentaLoRespondido()
        {
            var registro = new DateTime(2026, 10, 1, 8, 0, 0);
            var novedades = DatosNovedades.De(new[]
            {
                new NovedadPedido { TipoNovedad = TiposNovedad.Faltante, FechaRegistro = registro, Atendida = true, FechaAtencion = registro.AddHours(2) },
                new NovedadPedido { TipoNovedad = TiposNovedad.Faltante, FechaRegistro = registro, Atendida = true, FechaAtencion = registro.AddHours(4) },
                new NovedadPedido { TipoNovedad = TiposNovedad.Averia, FechaRegistro = registro }
            });
            Assert.Equal(1, novedades.SinAtender);
            Assert.Equal(3, novedades.HorasPromedioAtencion);
            Assert.Equal(TiposNovedad.Faltante, novedades.PorTipo[0].Grupo); // la más frecuente primero
            Assert.Single(novedades.SinAtenderDetalle);

            var solicitudes = DatosSolicitudes.De(new[]
            {
                new Solicitud { Estado = EstadosSolicitud.Aprobada, FechaSolicitud = registro, FechaRespuesta = registro.AddHours(1) },
                new Solicitud { Estado = EstadosSolicitud.Rechazada, FechaSolicitud = registro, FechaRespuesta = registro.AddHours(3) },
                new Solicitud { Estado = EstadosSolicitud.Pendiente, FechaSolicitud = registro }
            });
            Assert.Equal(1, solicitudes.Totales.Pendientes);
            Assert.Equal(50, solicitudes.Totales.PorcentajeAprobacion);
            Assert.Equal(2, solicitudes.Totales.HorasPromedioRespuesta);
        }

        [Theory]
        [InlineData(8, 17, 8, true)]
        [InlineData(8, 17, 17, false)]   // a la hora de salida ya no está
        [InlineData(22, 6, 23, true)]    // turno nocturno: cruza la medianoche
        [InlineData(22, 6, 3, true)]
        [InlineData(22, 6, 12, false)]
        public void EmpleadoEnTurnoConsideraLosTurnosNocturnos(int entrada, int salida, int hora, bool enTurno)
        {
            var empleado = new Empleado { HorarioEntrada = new(entrada, 0, 0), HorarioSalida = new(salida, 0, 0) };
            Assert.Equal(enTurno, DatosCargaHoraria.EnTurno(empleado, hora));
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
