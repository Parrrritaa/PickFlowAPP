using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using PickingApp.Controllers;
using PickingApp.Models;
using PickingApp.Models.ViewModels;
using PickingApp.Services;

namespace PickingApp.Tests
{
    public class ReportesHomeControllerTests
    {
        private static ReportService Pdf() => new(new ReportesPdfTests.Entorno(), NullLogger<ReportService>.Instance);

        private static ReportesController Reportes(Escenario e, int rol, out Ctl.SinAuditoria auditoria)
        {
            auditoria = new Ctl.SinAuditoria();
            var bodega = rol == Roles.IdAdministrador ? null : e.Db.Bodegas.First();
            return Ctl.Con(new ReportesController(e.Db, Pdf(), auditoria), Ctl.Usuario(e, rol, bodega));
        }

        private static HomeController Home(Escenario e, int rol = Roles.IdAdministrador)
        {
            var bodega = rol == Roles.IdAdministrador ? null : e.Db.Bodegas.First();
            return Ctl.Con(new HomeController(e.Db, Pdf(), new Ctl.SinAuditoria()), Ctl.Usuario(e, rol, bodega));
        }

        private static string? Error(Controller c) => c.TempData["ErrorMessage"] as string;

        private static void Datos(Escenario e)
        {
            var empleado = e.Empleado();
            var u = e.Ubicacion(Escenario.Pequeno);
            e.Completado(empleado, 20, 25, DateTime.Today);
            var pedido = e.Pedido(PedidoEstados.Cancelado, (u, 3));
            pedido.MotivoReasignacion = "Cambio de turno";
            e.Pedido(PedidoEstados.Pendiente, (u, 2));
            e.Db.NovedadesPedido.Add(new NovedadPedido { PedidoId = pedido.Id, TipoNovedad = TiposNovedad.Faltante, Descripcion = "x", RegistradoPor = "ana" });
            e.Db.Solicitudes.Add(Solicitud.Nueva(TiposSolicitud.RegistroEmpleado, Ctl.Usuario(e, Roles.IdAuxiliar).Id, new DatosEmpleadoSolicitud()));
            e.Db.SaveChanges();
        }

        private static void EsPdf(IActionResult r)
        {
            var archivo = Assert.IsType<FileContentResult>(r);
            Assert.Equal("application/pdf", archivo.ContentType);
            Assert.NotEmpty(archivo.FileContents);
        }

        // ---------- Reportes ----------

        [Fact]
        public async Task CadaReporteGeneraUnPdfConLosDatosDelPeriodo()
        {
            using var e = new Escenario();
            Datos(e);
            var c = Reportes(e, Roles.IdAdministrador, out var auditoria);
            var empleadoId = e.Db.Empleados.First().Id;
            var bodegaId = e.Db.Bodegas.First().Id;
            var desde = DateTime.Today.AddDays(-5);
            var hasta = DateTime.Today.AddDays(1);

            Assert.IsType<ViewResult>(await c.Index());
            EsPdf(await c.TiemposPorEmpleado(null, desde, hasta));
            EsPdf(await c.TiemposPorEmpleado(empleadoId, desde, hasta));
            EsPdf(await c.TiemposGenerales(null, desde, hasta));
            EsPdf(await c.TiemposGenerales(bodegaId, null, null));
            EsPdf(await c.PedidosPorPeriodo(null, desde, hasta));
            EsPdf(await c.PedidosPorPeriodo(bodegaId, null, null));
            EsPdf(await c.CompletadosCanceladosReasignados(null, desde, hasta));
            EsPdf(await c.CompletadosCanceladosReasignados(PedidoEstados.Reasignado, null, null));
            EsPdf(await c.CompletadosCanceladosReasignados(PedidoEstados.Cancelado, null, null));
            EsPdf(await c.Novedades(desde, hasta));
            EsPdf(await c.Solicitudes(desde, hasta));
            EsPdf(await c.ProductosUbicaciones(null, desde, hasta));
            EsPdf(await c.ProductosUbicaciones(bodegaId, null, null));
            EsPdf(await c.CargaHoraria(null, desde, hasta));
            EsPdf(await c.CargaHoraria(bodegaId, null, null));
            Assert.Equal(15, auditoria.Acciones.Count);
        }

        [Fact]
        public async Task LosFiltrosInvalidosVuelvenAReportes()
        {
            using var e = new Escenario();
            var c = Reportes(e, Roles.IdAdministrador, out _);
            var antes = DateTime.Today.AddDays(-3);
            var despues = DateTime.Today;

            async Task Redirige(Task<IActionResult> accion, string mensaje)
            {
                var r = Assert.IsType<RedirectToActionResult>(await accion);
                Assert.Equal("Index", r.ActionName);
                Assert.Contains(mensaje, Error(c));
            }

            await Redirige(c.TiemposPorEmpleado(null, despues, antes), "fecha inicial");
            await Redirige(c.TiemposPorEmpleado(9999, null, null), "empleado seleccionado");
            await Redirige(c.TiemposGenerales(null, despues, antes), "fecha inicial");
            await Redirige(c.TiemposGenerales(9999, null, null), "bodega seleccionada");
            await Redirige(c.PedidosPorPeriodo(null, despues, antes), "fecha inicial");
            await Redirige(c.PedidosPorPeriodo(9999, null, null), "bodega seleccionada");
            await Redirige(c.CompletadosCanceladosReasignados(null, despues, antes), "fecha inicial");
            await Redirige(c.CompletadosCanceladosReasignados("Inventado", null, null), "estado seleccionado");
            await Redirige(c.Novedades(despues, antes), "fecha inicial");
            await Redirige(c.Solicitudes(despues, antes), "fecha inicial");
            await Redirige(c.ProductosUbicaciones(null, despues, antes), "fecha inicial");
            await Redirige(c.ProductosUbicaciones(9999, null, null), "bodega seleccionada");
            await Redirige(c.CargaHoraria(null, despues, antes), "fecha inicial");
            await Redirige(c.CargaHoraria(9999, null, null), "bodega seleccionada");
        }

        [Fact]
        public async Task ElSupervisorSiempreRecibeLosReportesDeSuBodega()
        {
            using var e = new Escenario();
            Datos(e);
            var otra = new Bodega { Codigo = "BOD-55", Nombre = "Ajena" };
            e.Db.Bodegas.Add(otra);
            e.Db.SaveChanges();
            var c = Reportes(e, Roles.IdSupervisor, out _);

            // Aunque pida otra bodega por la URL, el reporte es de la suya
            var archivo = Assert.IsType<FileContentResult>(await c.PedidosPorPeriodo(otra.Id, null, null));
            Assert.Contains("BOD-01", archivo.FileDownloadName);
        }

        // ---------- Home ----------

        [Fact]
        public async Task ElDashboardCuentaLosPedidosPorEstado()
        {
            using var e = new Escenario();
            Datos(e);

            var modelo = (DashboardViewModel)((ViewResult)await Home(e).Index()).Model!;

            Assert.Equal(1, modelo.TotalPendientes);
            Assert.Equal(1, modelo.TotalCompletados);
            Assert.Equal(1, modelo.TotalCancelados);
            Assert.Equal(3, modelo.UltimosPedidos.Count);
        }

        [Theory]
        [InlineData("estado")]
        [InlineData("prioridad")]
        [InlineData("bodega")]
        [InlineData("empleado")]
        [InlineData("dia")]
        public async Task LaGraficaAgrupaPorCadaDimension(string agrupar)
        {
            using var e = new Escenario();
            Datos(e);

            var json = Assert.IsType<JsonResult>(await Home(e).DatosGrafica(agrupar, 7, e.Db.Bodegas.First().Id, Prioridades.Todas.First(), "Pendiente,Completado"));

            Assert.NotNull(json.Value);
        }

        [Fact]
        public async Task LaGraficaSinFiltrosYSinDatos()
        {
            using var e = new Escenario();
            Assert.IsType<JsonResult>(await Home(e).DatosGrafica("dia"));
            Assert.IsType<JsonResult>(await Home(e).DatosGrafica("estado"));
        }

        [Fact]
        public async Task ExportarDashboardPdfAceptaSoloUnPngValido()
        {
            using var e = new Escenario();
            Datos(e);
            var c = Home(e);
            var png = "data:image/png;base64," + Convert.ToBase64String(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4 });

            // Valores inválidos se corrigen; la imagen no válida se ignora
            EsPdf(await c.ExportarDashboardPdf("raro", 99999, e.Db.Bodegas.First().Id, "Alta", "Pendiente,Asignado", "raro", "data:image/png;base64,@@@"));
            EsPdf(await c.ExportarDashboardPdf("bodega", 30, null, null, "-", "bar", png));
            EsPdf(await c.ExportarDashboardPdf("estado", 0, null, null, null, "doughnut", "no-es-imagen"));
            EsPdf(await Home(e, Roles.IdSupervisor).ExportarDashboardPdf());
        }

        [Fact]
        public void LasPaginasDeErrorSeResuelven()
        {
            using var e = new Escenario();
            var c = Home(e);

            Assert.IsType<ViewResult>(c.Error());
            Assert.IsType<ViewResult>(c.Estado(404));
        }

        [Fact]
        public void ErrorBaseDatosVuelveAlInicioSinErrorOMuestraElAviso()
        {
            using var e = new Escenario();
            var c = Home(e);

            Assert.IsType<RedirectToActionResult>(c.ErrorBaseDatos());

            c.HttpContext.Features.Set<IExceptionHandlerPathFeature>(new ExceptionHandlerFeature { Error = new Exception("boom"), Path = "/Pedidos" });
            c.HttpContext.Request.Method = "GET";
            var get = Assert.IsType<ViewResult>(c.ErrorBaseDatos());
            Assert.Equal("/Pedidos", ((ErrorBaseDatosViewModel)get.Model!).UrlReintento);

            c.HttpContext.Request.Method = "POST";
            c.Url = new FakeUrl();
            var post = Assert.IsType<ViewResult>(c.ErrorBaseDatos());
            Assert.Equal("/", ((ErrorBaseDatosViewModel)post.Model!).UrlReintento);
        }

        private sealed class FakeUrl : IUrlHelper
        {
            public ActionContext ActionContext => new();
            public string? Action(Microsoft.AspNetCore.Mvc.Routing.UrlActionContext actionContext) => "/";
            public string? Content(string? contentPath) => contentPath;
            public bool IsLocalUrl(string? url) => true;
            public string? Link(string? routeName, object? values) => "/";
            public string? RouteUrl(Microsoft.AspNetCore.Mvc.Routing.UrlRouteContext routeContext) => "/";
        }
    }
}
