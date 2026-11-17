using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickingApp.Controllers;
using PickingApp.Models;
using PickingApp.Models.ViewModels;
using PickingApp.Services;

namespace PickingApp.Tests
{
    public class PedidosControllerTests
    {
        private sealed class ExcelFalso : IExcelService
        {
            public ExcelValidationResult Resultado { get; set; } = new();
            public Task<ExcelValidationResult> ValidarArchivoExcelAsync(Stream stream, IReadOnlyList<ResolucionExcel> resoluciones, int? bodegaDelUsuario) =>
                Task.FromResult(Resultado);
        }

        private static PedidosController Como(Escenario e, out Ctl.SinAuditoria auditoria, ExcelFalso? excel = null, int rol = Roles.IdAdministrador)
        {
            auditoria = new Ctl.SinAuditoria();
            var bodega = rol == Roles.IdAdministrador ? null : e.Db.Bodegas.First();
            return Ctl.Con(new PedidosController(e.Db, excel ?? new ExcelFalso(), e.Motor(), auditoria), Ctl.Usuario(e, rol, bodega));
        }

        private static string? Error(Controller c) => c.TempData["ErrorMessage"] as string;
        private static string? Exito(Controller c) => c.TempData["SuccessMessage"] as string;
        private static string Destino(IActionResult r) => ((RedirectToActionResult)r).ActionName!;

        private static IFormFile Archivo(string nombre, int bytes = 10) =>
            new FormFile(new MemoryStream(new byte[bytes]), 0, bytes, "archivoExcel", nombre);

        // ---------- Consulta ----------

        [Fact]
        public async Task IndexCombinaFiltrosYCuentaPorEstado()
        {
            using var e = new Escenario();
            var u = e.Ubicacion(Escenario.Pequeno);
            var empleado = e.Empleado();
            var pedido = e.Pedido(PedidoEstados.Pendiente, (u, 2));
            e.Pedido(PedidoEstados.Completado, (u, 1));
            e.Db.NovedadesPedido.Add(new NovedadPedido { PedidoId = pedido.Id, TipoNovedad = TiposNovedad.Todos.First(), Descripcion = "x", RegistradoPor = "a" });
            e.Db.SaveChanges();
            var c = Como(e, out _);

            var simple = (PedidosIndexViewModel)((ViewResult)await c.Index(null, null, null, null, null, null, null, null, null, null)).Model!;
            Assert.Equal(2, simple.Pedidos.Count);

            var filtrado = (PedidosIndexViewModel)((ViewResult)await c.Index(
                PedidoEstados.Pendiente, Prioridades.Todas.First(), e.Db.Bodegas.First().Id, "PROD", -1,
                DateTime.Today.AddDays(-1), DateTime.Today.AddDays(1), null, 1, FiltrosNovedad.Todas)).Model!;
            Assert.Equal(PedidoEstados.Pendiente, filtrado.FiltroEstado);

            Assert.IsType<ViewResult>(await c.Index(null, null, null, null, empleado.Id, null, null, "divididos", null, TiposNovedad.Todos.First()));
            Assert.IsType<ViewResult>(await c.Index(null, null, null, null, -1, null, null, "divididos", null, "pendientes"));
        }

        [Fact]
        public async Task DetalleEImprimirEncuentranElPedidoSoloDentroDelAlcance()
        {
            using var e = new Escenario();
            var u = e.Ubicacion(Escenario.Pequeno);
            var pedido = e.Pedido(PedidoEstados.Pendiente, (u, 2));
            var c = Como(e, out _);

            Assert.IsType<ViewResult>(await c.Detalle(pedido.Id));
            Assert.IsType<NotFoundResult>(await c.Detalle(9999));
            Assert.IsType<ViewResult>(await c.Imprimir(pedido.Id));
            Assert.Equal("SAMEORIGIN", c.Response.Headers["X-Frame-Options"]);
            Assert.IsType<NotFoundResult>(await c.Imprimir(9999));

            // Un supervisor de otra bodega no lo ve
            var otra = new Bodega { Codigo = "BOD-77", Nombre = "Otra" };
            e.Db.Bodegas.Add(otra);
            e.Db.SaveChanges();
            var ajeno = Ctl.Con(new PedidosController(e.Db, new ExcelFalso(), e.Motor(), new Ctl.SinAuditoria()), Ctl.Usuario(e, Roles.IdSupervisor, otra));
            Assert.IsType<NotFoundResult>(await ajeno.Detalle(pedido.Id));
        }

        // ---------- Completar, cancelar, prioridad ----------

        [Fact]
        public async Task MarcarCompletadoExigeConfirmacionYUnPedidoEnCurso()
        {
            using var e = new Escenario();
            var u = e.Ubicacion(Escenario.Pequeno);
            var libre = e.Pedido(PedidoEstados.Pendiente, (u, 1));
            var enCurso = e.Pedido(PedidoEstados.Asignado, (u, 1));
            enCurso.EmpleadoId = e.Empleado().Id;
            enCurso.FechaAsignacion = DateTime.Now.AddMinutes(-30);
            e.Db.SaveChanges();
            var c = Como(e, out var auditoria);

            await c.MarcarCompletado(libre.Id, false);
            Assert.Contains("confirmación física", Error(c));
            Assert.IsType<NotFoundResult>(await c.MarcarCompletado(9999, true));
            await c.MarcarCompletado(libre.Id, true);
            Assert.Contains("asignado a un empleado", Error(c));

            await c.MarcarCompletado(enCurso.Id, true);
            var guardado = e.Db.Pedidos.Single(p => p.Id == enCurso.Id);
            Assert.Equal(PedidoEstados.Completado, guardado.Estado);
            Assert.NotNull(guardado.TiempoProcesamientoMinutos);
            Assert.Contains("Completar", auditoria.Acciones.Single());
        }

        [Fact]
        public async Task CancelarUnPedidoYSusPartes()
        {
            using var e = new Escenario();
            var u = e.Ubicacion(Escenario.Pequeno);
            var pendiente = e.Pedido(PedidoEstados.Pendiente, (u, 4));
            var cancelado = e.Pedido(PedidoEstados.Cancelado, (u, 1));
            var completado = e.Pedido(PedidoEstados.Completado, (u, 1));
            var c = Como(e, out _);

            c.ModelState.AddModelError("motivoCancelacion", "El motivo de cancelación es obligatorio.");
            await c.Cancelar(pendiente.Id, "");
            Assert.Contains("obligatorio", Error(c));
            c.ModelState.Clear();

            Assert.IsType<NotFoundResult>(await c.Cancelar(9999, "x"));
            await c.Cancelar(cancelado.Id, "x");
            Assert.Contains("ya se encuentra cancelado", Error(c));
            await c.Cancelar(completado.Id, "x");
            Assert.Contains("completado", Error(c));

            // Dividir y cancelar el original arrastra las partes sin terminar
            var cantidades = e.Db.DetallesPedido.Where(d => d.PedidoId == pendiente.Id).ToDictionary(d => $"{d.Id}_1", _ => "2");
            foreach (var d in e.Db.DetallesPedido.Where(d => d.PedidoId == pendiente.Id)) cantidades[$"{d.Id}_2"] = "2";
            await c.Dividir(pendiente.Id, 2, cantidades);
            await c.Cancelar(pendiente.Id, "Cliente anuló");

            Assert.Contains("2 parte(s)", Exito(c));
            Assert.All(e.Db.Pedidos.Where(p => p.PedidoPadreId == pendiente.Id), p => Assert.Equal(PedidoEstados.Cancelado, p.Estado));
        }

        [Fact]
        public async Task CambiarPrioridadTambienLlegaALasPartes()
        {
            using var e = new Escenario();
            var u = e.Ubicacion(Escenario.Pequeno);
            var pedido = e.Pedido(PedidoEstados.Pendiente, (u, 4));
            var c = Como(e, out _);
            var nueva = Prioridades.Todas.First(p => p != pedido.Prioridad);

            await c.CambiarPrioridad(pedido.Id, "Inventada");
            Assert.Contains("prioridad válida", Error(c));
            Assert.IsType<NotFoundResult>(await c.CambiarPrioridad(9999, nueva));

            var detalle = e.Db.DetallesPedido.Single(d => d.PedidoId == pedido.Id);
            await c.Dividir(pedido.Id, 2, new Dictionary<string, string> { [$"{detalle.Id}_1"] = "2", [$"{detalle.Id}_2"] = "2" });
            await c.CambiarPrioridad(pedido.Id, nueva);

            Assert.Equal(nueva, e.Db.Pedidos.Single(p => p.Id == pedido.Id).Prioridad);
            Assert.All(e.Db.Pedidos.Where(p => p.PedidoPadreId == pedido.Id), p => Assert.Equal(nueva, p.Prioridad));
        }

        // ---------- División ----------

        [Fact]
        public async Task DividirValidaElRepartoAntesDeCrearLasPartes()
        {
            using var e = new Escenario();
            var u = e.Ubicacion(Escenario.Pequeno);
            var pedido = e.Pedido(PedidoEstados.Pendiente, (u, 4));
            var d = e.Db.DetallesPedido.Single(x => x.PedidoId == pedido.Id);
            var c = Como(e, out var auditoria);

            Assert.IsType<NotFoundResult>(await c.Dividir(9999));
            Assert.IsType<ViewResult>(await c.Dividir(pedido.Id));
            Assert.IsType<NotFoundResult>(await c.Dividir(9999, 2, null));

            await c.Dividir(pedido.Id, 1, null);
            Assert.Contains("entre 2 y", Error(c));

            await c.Dividir(pedido.Id, 2, new() { [$"{d.Id}_1"] = "x" });
            Assert.Contains("no es válida", Error(c));

            await c.Dividir(pedido.Id, 2, new() { [$"{d.Id}_1"] = "1", [$"{d.Id}_2"] = "1" });
            Assert.Contains("se repartieron 2", Error(c));

            await c.Dividir(pedido.Id, 2, new() { [$"{d.Id}_1"] = "4" });
            Assert.Contains("quedó sin productos", Error(c));
            Assert.Empty(auditoria.Acciones);

            await c.Dividir(pedido.Id, 2, new() { [$"{d.Id}_1"] = "3", [$"{d.Id}_2"] = "1" });
            Assert.Equal(PedidoEstados.Dividido, e.Db.Pedidos.Single(p => p.Id == pedido.Id).Estado);
            Assert.Equal(2, e.Db.Pedidos.Count(p => p.PedidoPadreId == pedido.Id));

            // Ya dividido: no se divide de nuevo
            await c.Dividir(pedido.Id, 2, null);
            Assert.Contains("pendientes", Error(c));
            Assert.Equal("Detalle", Destino(await c.Dividir(pedido.Id)));
        }

        [Fact]
        public async Task SeDeshaceLaDivisionMientrasNadieHayaTrabajadoLasPartes()
        {
            using var e = new Escenario();
            var u = e.Ubicacion(Escenario.Pequeno);
            var pedido = e.Pedido(PedidoEstados.Pendiente, (u, 2));
            var d = e.Db.DetallesPedido.Single(x => x.PedidoId == pedido.Id);
            var c = Como(e, out _);

            Assert.IsType<NotFoundResult>(await c.DeshacerDivision(9999));
            await c.DeshacerDivision(pedido.Id);
            Assert.Contains("no está dividido", Error(c));

            await c.Dividir(pedido.Id, 2, new() { [$"{d.Id}_1"] = "1", [$"{d.Id}_2"] = "1" });
            var parte = e.Db.Pedidos.First(p => p.PedidoPadreId == pedido.Id);
            parte.Estado = PedidoEstados.Asignado;
            e.Db.SaveChanges();
            await c.DeshacerDivision(pedido.Id);
            Assert.Contains("No se puede deshacer", Error(c));

            parte.Estado = PedidoEstados.Pendiente;
            e.Db.SaveChanges();
            await c.DeshacerDivision(pedido.Id);
            Assert.Equal(PedidoEstados.Pendiente, e.Db.Pedidos.Single(p => p.Id == pedido.Id).Estado);
            Assert.Empty(e.Db.Pedidos.Where(p => p.PedidoPadreId == pedido.Id));
        }

        // ---------- Asignación ----------

        [Fact]
        public async Task AsignarMuestraLosEmpleadosYRespetaLasReglasDelPedido()
        {
            using var e = new Escenario();
            var u = e.Ubicacion(Escenario.Pequeno);
            var pedido = e.Pedido(PedidoEstados.Pendiente, (u, 2));
            e.Empleado();
            e.Empleado(activo: false);
            var c = Como(e, out _);

            var vista = (AsignarPedidoViewModel)((ViewResult)await c.Asignar(pedido.Id)).Model!;
            Assert.Single(vista.Empleados);
            Assert.NotNull(vista.EmpleadoRecomendadoId);
            Assert.IsType<NotFoundResult>(await c.Asignar(9999));

            var dividido = e.Pedido(PedidoEstados.Dividido, (u, 2));
            Assert.Equal("Detalle", Destino(await c.Asignar(dividido.Id)));
        }

        [Fact]
        public async Task ReasignarExigeMotivoYUnPedidoEnCurso()
        {
            using var e = new Escenario();
            var u = e.Ubicacion(Escenario.Pequeno);
            var pedido = e.Pedido(PedidoEstados.Pendiente, (u, 2));
            var c = Como(e, out _);

            await c.Reasignar(pedido.Id, 1, "  ");
            Assert.Contains("obligatorio", Error(c));
            await c.Reasignar(pedido.Id, 1, new string('x', 251));
            Assert.Contains("no puede superar", Error(c));
        }

        // ---------- Carga de Excel ----------

        [Fact]
        public async Task CargarExcelRechazaArchivosInvalidos()
        {
            using var e = new Escenario();
            var c = Como(e, out _);

            Assert.IsType<ViewResult>(c.CargarExcel());
            Assert.IsType<ViewResult>(await c.CargarExcel(null!, null));
            Assert.Contains("No fue posible", Error(c));

            Assert.IsType<ViewResult>(await c.CargarExcel(Archivo("pedidos.csv"), null));
            Assert.Contains(".xlsx", Error(c));

            Assert.IsType<ViewResult>(await c.CargarExcel(Archivo("pedidos.xlsx", 5 * 1024 * 1024 + 1), null));
            Assert.Contains("5 MB", Error(c));
        }

        [Fact]
        public async Task CargarExcelConErroresMuestraLasObservaciones()
        {
            using var e = new Escenario();
            var excel = new ExcelFalso { Resultado = new ExcelValidationResult { IsValid = false, Errors = { "Fila 2: cantidad inválida" } } };
            var c = Como(e, out _, excel);

            var vista = Assert.IsType<ViewResult>(await c.CargarExcel(Archivo("pedidos.xlsx"), null));

            Assert.Equal("Fila 2: cantidad inválida", Assert.Single((List<string>)vista.Model!));

            excel.Resultado = new ExcelValidationResult { IsValid = false };
            var sinDetalle = Assert.IsType<ViewResult>(await c.CargarExcel(Archivo("pedidos.xlsx"), null));
            Assert.Contains("sin confirmar", Assert.Single((List<string>)sinDetalle.Model!));
        }

        [Fact]
        public async Task CargarExcelValidoGuardaLosPedidosYLasNovedades()
        {
            using var e = new Escenario();
            var u = e.Ubicacion(Escenario.Pequeno);
            var bodega = e.Db.Bodegas.First();
            var pedido = new Pedido { CodigoPedido = "PED-EXCEL-1", BodegaId = bodega.Id, Estado = PedidoEstados.Pendiente };
            pedido.Detalles.Add(new DetallePedido { UbicacionId = u.Id, Cantidad = 2, CodigoProducto = "PROD-001", NombreProducto = "Producto 1" });
            pedido.Novedades.Add(new NovedadPedido { TipoNovedad = TiposNovedad.Todos.First(), Descripcion = "No disponible" });
            var excel = new ExcelFalso { Resultado = new ExcelValidationResult { IsValid = true, PedidosValidados = { pedido }, TotalPedidos = 1, TotalFilas = 1 } };
            var c = Como(e, out var auditoria, excel);

            var resultado = await c.CargarExcel(Archivo("pedidos.xlsx"), null);

            Assert.Equal("Index", Destino(resultado));
            Assert.Contains("1 pedidos", Exito(c));
            Assert.Contains(e.Db.Pedidos, p => p.CodigoPedido == "PED-EXCEL-1");
            Assert.Single(auditoria.Acciones);
            Assert.False(string.IsNullOrEmpty(e.Db.NovedadesPedido.Single().RegistradoPor));
        }

        [Fact]
        public async Task ValidarExcelResponderEnJson()
        {
            using var e = new Escenario();
            var excel = new ExcelFalso { Resultado = new ExcelValidationResult { IsValid = true, ArchivoLegible = true, TotalPedidos = 3 } };
            var c = Como(e, out _, excel);

            Assert.IsType<JsonResult>(await c.ValidarExcel(null!, null));
            Assert.IsType<JsonResult>(await c.ValidarExcel(Archivo("a.txt"), null));
            Assert.IsType<JsonResult>(await c.ValidarExcel(Archivo("a.xlsx", 5 * 1024 * 1024 + 1), null));
            var ok = Assert.IsType<JsonResult>(await c.ValidarExcel(Archivo("a.xlsx"), null));
            Assert.Contains("totalPedidos = 3", ok.Value!.ToString());
        }
    }
}
