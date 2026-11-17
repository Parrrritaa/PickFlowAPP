using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickingApp.Controllers;
using PickingApp.Models;
using PickingApp.Models.ViewModels;

namespace PickingApp.Tests
{
    public class BodegasControllerTests
    {
        private static BodegasController Como(Escenario e, int rol, out Ctl.SinAuditoria auditoria, Bodega? bodega = null)
        {
            auditoria = new Ctl.SinAuditoria();
            return Ctl.Con(new BodegasController(e.Db, auditoria), Ctl.Usuario(e, rol, bodega));
        }

        private static BodegasController Admin(Escenario e) => Como(e, Roles.IdAdministrador, out _);
        private static BodegasController Supervisor(Escenario e, out Ctl.SinAuditoria a) => Como(e, Roles.IdSupervisor, out a, e.Db.Bodegas.First());

        private static UbicacionCampos Campos(string nivel = "9", bool activa = true) =>
            new() { Pasillo = "1", Estante = "1", Nivel = nivel, Activa = activa };

        private static SeleccionProductoViewModel Nuevo(string numero = "77", string nombre = "Producto nuevo") =>
            new() { Modo = SeleccionProductoViewModel.ModoNuevo, NumeroProducto = numero, NombreProducto = nombre, UnidadesPorCaja = "4" };

        private static string? Error(Controller c) => c.TempData["ErrorMessage"] as string;
        private static string? Exito(Controller c) => c.TempData["SuccessMessage"] as string;

        [Fact]
        public async Task IndexDelAdministradorFiltraPorEstadoYBuscaProductos()
        {
            using var e = new Escenario();
            e.Ubicacion(Escenario.Pequeno);
            e.Db.Bodegas.Add(new Bodega { Codigo = "BOD-02", Nombre = "Inactiva", Activa = false });
            e.Db.SaveChanges();
            var c = Admin(e);

            var todas = (BodegasIndexViewModel)((ViewResult)await c.Index(null, null)).Model!;
            Assert.Equal(2, todas.Bodegas.Count);

            var activas = (BodegasIndexViewModel)((ViewResult)await c.Index("Activa", null)).Model!;
            Assert.Single(activas.Bodegas);

            var inactivas = (BodegasIndexViewModel)((ViewResult)await c.Index("Inactiva", "PROD-001")).Model!;
            Assert.Equal("BOD-02", inactivas.Bodegas.Single().Codigo);
            Assert.Single(inactivas.ResultadosProducto!);
        }

        [Fact]
        public async Task ElSupervisorVaDirectoAlDetalleDeSuBodega()
        {
            using var e = new Escenario();
            var bodega = e.Db.Bodegas.First();

            var redireccion = Assert.IsType<RedirectToActionResult>(await Supervisor(e, out _).Index(null, null));

            Assert.Equal("Detalle", redireccion.ActionName);
            Assert.Equal(bodega.Id, redireccion.RouteValues!["id"]);
        }

        [Fact]
        public async Task DetalleFiltraUbicacionesYNoMuestraBodegasAjenas()
        {
            using var e = new Escenario();
            e.Ubicacion(Escenario.Pequeno);
            e.Ubicacion(Escenario.Grande);
            var bodega = e.Db.Bodegas.First();
            var c = Admin(e);

            var sinFiltro = (BodegaDetalleViewModel)((ViewResult)await c.Detalle(bodega.Id, null, null)).Model!;
            Assert.Equal(2, sinFiltro.Bodega.Ubicaciones.Count);

            var porCodigo = (BodegaDetalleViewModel)((ViewResult)await c.Detalle(bodega.Id, "NIV02", null)).Model!;
            Assert.Single(porCodigo.Bodega.Ubicaciones);

            var porProducto = (BodegaDetalleViewModel)((ViewResult)await c.Detalle(bodega.Id, null, "producto 1")).Model!;
            Assert.Single(porProducto.Bodega.Ubicaciones);

            Assert.IsType<NotFoundResult>(await c.Detalle(9999, null, null));
            var otra = new Bodega { Codigo = "BOD-09", Nombre = "Otra" };
            e.Db.Bodegas.Add(otra);
            e.Db.SaveChanges();
            Assert.IsType<NotFoundResult>(await Supervisor(e, out _).Detalle(otra.Id, null, null));
        }

        [Fact]
        public async Task CrearBodegaGeneraElSiguienteCodigo()
        {
            using var e = new Escenario();
            var c = Como(e, Roles.IdAdministrador, out var auditoria);

            Assert.IsType<ViewResult>(c.Crear());
            c.ModelState.AddModelError("Nombre", "obligatorio");
            Assert.IsType<ViewResult>(await c.Crear(new BodegaCampos()));
            c.ModelState.Clear();

            await c.Crear(new BodegaCampos { Nombre = " Norte ", Activa = true });

            var creada = e.Db.Bodegas.Single(b => b.Nombre == "Norte");
            Assert.Equal("BOD-02", creada.Codigo);
            Assert.Contains("Crear Bodega", auditoria.Acciones);
        }

        [Fact]
        public async Task EditarBodegaActualizaSusDatos()
        {
            using var e = new Escenario();
            var bodega = e.Db.Bodegas.First();
            var c = Admin(e);

            Assert.IsType<NotFoundResult>(await c.Editar(9999));
            Assert.IsType<NotFoundResult>(await c.Editar(9999, new BodegaCampos()));
            var vista = Assert.IsType<ViewResult>(await c.Editar(bodega.Id));
            Assert.Equal("BOD-01", ((BodegaCampos)vista.Model!).Codigo);

            c.ModelState.AddModelError("Nombre", "obligatorio");
            var invalida = Assert.IsType<ViewResult>(await c.Editar(bodega.Id, new BodegaCampos()));
            Assert.Equal("BOD-01", ((BodegaCampos)invalida.Model!).Codigo);
            c.ModelState.Clear();

            await c.Editar(bodega.Id, new BodegaCampos { Nombre = "Renombrada", Activa = false });
            Assert.Equal("Renombrada", e.Db.Bodegas.Single().Nombre);
            Assert.False(e.Db.Bodegas.Single().Activa);
        }

        [Fact]
        public async Task EliminarBodegaSoloSiNoTieneNada()
        {
            using var e = new Escenario();
            var c = Admin(e);
            Assert.IsType<NotFoundResult>(await c.EliminarBodega(9999));

            e.Ubicacion(Escenario.Pequeno);
            var conUbicacion = e.Db.Bodegas.First();
            await c.EliminarBodega(conUbicacion.Id);
            Assert.Contains("No se puede eliminar", Error(c));

            var vacia = new Bodega { Codigo = "BOD-05", Nombre = "Vacía" };
            e.Db.Bodegas.Add(vacia);
            e.Db.SaveChanges();
            await c.EliminarBodega(vacia.Id);
            Assert.Contains("eliminada", Exito(c));
            Assert.DoesNotContain(e.Db.Bodegas, b => b.Id == vacia.Id);
        }

        [Fact]
        public async Task CrearUbicacionConProductoNuevoYConExistente()
        {
            using var e = new Escenario();
            var bodega = e.Db.Bodegas.First();
            var c = Como(e, Roles.IdAdministrador, out var auditoria);

            Assert.IsType<NotFoundResult>(await c.CrearUbicacion(9999));
            Assert.IsType<ViewResult>(await c.CrearUbicacion(bodega.Id));
            Assert.IsType<NotFoundResult>(await c.CrearUbicacion(9999, Campos(), Nuevo()));

            await c.CrearUbicacion(bodega.Id, Campos("9"), Nuevo());
            var producto = e.Db.Productos.Single(p => p.Codigo == "PROD-077");
            Assert.Equal(4, producto.UnidadesPorCaja);
            Assert.Contains("producto nuevo", Exito(c));

            // Mismo código de ubicación: vuelve al formulario con el error
            var repetida = Assert.IsType<ViewResult>(await c.CrearUbicacion(bodega.Id, Campos("9"), Nuevo("78", "Otro")));
            Assert.False(c.ModelState.IsValid);
            Assert.IsType<UbicacionFormViewModel>(repetida.Model);

            c.ModelState.Clear();
            await c.CrearUbicacion(bodega.Id, Campos("10"), new SeleccionProductoViewModel { ProductoId = producto.Id });
            Assert.Equal(2, e.Db.Ubicaciones.Count(u => u.ProductoId == producto.Id));
            Assert.Contains("Crear Ubicación", auditoria.Acciones);
        }

        [Theory]
        [InlineData("abc", "Nombre", "4", "número de producto")]
        [InlineData("5", "", "4", "nombre de producto")]
        [InlineData("5", "Nombre", "0", "cantidad máxima")]
        public async Task ElProductoNuevoInvalidoVuelveAlFormulario(string numero, string nombre, string unidades, string mensaje)
        {
            using var e = new Escenario();
            var c = Admin(e);
            var seleccion = Nuevo(numero, nombre);
            seleccion.UnidadesPorCaja = unidades;

            Assert.IsType<ViewResult>(await c.CrearUbicacion(e.Db.Bodegas.First().Id, Campos(), seleccion));

            Assert.Contains(c.ModelState.Values.SelectMany(v => v.Errors), x => x.ErrorMessage.Contains(mensaje));
        }

        [Fact]
        public async Task ProductoNuevoRepetidoOSinSeleccionarNoSeGuarda()
        {
            using var e = new Escenario();
            e.Ubicacion(Escenario.Pequeno); // PROD-001
            var bodega = e.Db.Bodegas.First();
            var c = Admin(e);

            Assert.IsType<ViewResult>(await c.CrearUbicacion(bodega.Id, Campos(), Nuevo("1", "Repetido")));
            Assert.Contains(c.ModelState.Values.SelectMany(v => v.Errors), x => x.ErrorMessage.Contains("ya existe"));

            c.ModelState.Clear();
            Assert.IsType<ViewResult>(await c.CrearUbicacion(bodega.Id, Campos(), new SeleccionProductoViewModel()));
            Assert.Contains(c.ModelState.Values.SelectMany(v => v.Errors), x => x.ErrorMessage.Contains("Selecciona"));
        }

        [Fact]
        public async Task EditarUbicacionComoAdministrador()
        {
            using var e = new Escenario();
            var ubicacion = e.Ubicacion(Escenario.Pequeno);
            var otra = e.Ubicacion(Escenario.Grande);
            var c = Como(e, Roles.IdAdministrador, out var auditoria);

            Assert.IsType<NotFoundResult>(await c.EditarUbicacion(9999));
            Assert.IsType<ViewResult>(await c.EditarUbicacion(ubicacion.Id));
            Assert.IsType<NotFoundResult>(await c.EditarUbicacion(9999, Campos(), new(), null));

            // Código de otra ubicación de la misma bodega
            var choque = Campos(otra.Nivel);
            Assert.IsType<ViewResult>(await c.EditarUbicacion(ubicacion.Id, choque, new() { ProductoId = ubicacion.ProductoId }, null));
            Assert.False(c.ModelState.IsValid);

            c.ModelState.Clear();
            await c.EditarUbicacion(ubicacion.Id, Campos("20", false), new() { ProductoId = ubicacion.ProductoId }, null);

            var guardada = e.Db.Ubicaciones.Single(u => u.Id == ubicacion.Id);
            Assert.Equal("20", guardada.Nivel);
            Assert.False(guardada.Activa);
            Assert.Contains("Editar Ubicación", auditoria.Acciones);

            // Con producto nuevo
            await c.EditarUbicacion(otra.Id, Campos(otra.Nivel), Nuevo("55", "Cambiado"), null);
            Assert.Equal("PROD-055", e.Db.Ubicaciones.Include(u => u.Producto).Single(u => u.Id == otra.Id).Producto!.Codigo);
        }

        [Fact]
        public async Task NoSeCambiaLaUbicacionDeUnPedidoSinTerminar()
        {
            using var e = new Escenario();
            var ubicacion = e.Ubicacion(Escenario.Pequeno);
            e.Pedido(PedidoEstados.Pendiente, (ubicacion, 2));
            var c = Admin(e);

            Assert.IsType<ViewResult>(await c.EditarUbicacion(ubicacion.Id, Campos(ubicacion.Nivel, false), new() { ProductoId = ubicacion.ProductoId }, null));

            Assert.Contains(c.ModelState.Values.SelectMany(v => v.Errors), x => x.ErrorMessage.Contains("sin terminar"));
        }

        [Fact]
        public async Task ElSupervisorSolicitaLaEdicionConMotivo()
        {
            using var e = new Escenario();
            var ubicacion = e.Ubicacion(Escenario.Pequeno);
            var c = Supervisor(e, out var auditoria);
            var seleccion = new SeleccionProductoViewModel { ProductoId = ubicacion.ProductoId };

            // Sin motivo
            Assert.IsType<ViewResult>(await c.EditarUbicacion(ubicacion.Id, Campos("30"), seleccion, "  "));
            c.ModelState.Clear();

            // Sin cambios
            Assert.IsType<ViewResult>(await c.EditarUbicacion(ubicacion.Id, Campos(ubicacion.Nivel), seleccion, "motivo"));
            c.ModelState.Clear();

            await c.EditarUbicacion(ubicacion.Id, Campos("30"), seleccion, "Reubicación");
            Assert.Single(e.Db.Solicitudes);
            Assert.Contains("Requiere aprobación", Exito(c));
            Assert.Contains("Solicitud Edición Ubicación", auditoria.Acciones);

            // Ya hay una solicitud pendiente sobre la ubicación
            c.ModelState.Clear();
            Assert.IsType<ViewResult>(await c.EditarUbicacion(ubicacion.Id, Campos("31"), seleccion, "Otra"));
            Assert.Single(e.Db.Solicitudes);
        }

        [Fact]
        public async Task ElSupervisorSolicitaEdicionConProductoNuevo()
        {
            using var e = new Escenario();
            var ubicacion = e.Ubicacion(Escenario.Pequeno);
            var seleccion = Nuevo("88", "Del supervisor");
            seleccion.CategoriaProductoId = Escenario.Grande.ToString();

            await Supervisor(e, out _).EditarUbicacion(ubicacion.Id, Campos(ubicacion.Nivel), seleccion, "Producto distinto");

            Assert.Single(e.Db.Solicitudes);
            Assert.DoesNotContain(e.Db.Productos, p => p.Codigo == "PROD-088");
        }

        [Fact]
        public async Task EliminarUbicacionSegunElRol()
        {
            using var e = new Escenario();
            var libre = e.Ubicacion(Escenario.Pequeno);
            var conPedido = e.Ubicacion(Escenario.Grande);
            e.Pedido(PedidoEstados.Completado, (conPedido, 1));
            var admin = Admin(e);

            Assert.IsType<NotFoundResult>(await admin.EliminarUbicacion(9999, null));
            await admin.EliminarUbicacion(conPedido.Id, null);
            Assert.Contains("pedidos asociados", Error(admin));

            await admin.EliminarUbicacion(libre.Id, null);
            Assert.DoesNotContain(e.Db.Ubicaciones, u => u.Id == libre.Id);
        }

        [Fact]
        public async Task ElSupervisorSolicitaEliminarUnaUbicacion()
        {
            using var e = new Escenario();
            var ubicacion = e.Ubicacion(Escenario.Pequeno);
            var c = Supervisor(e, out _);

            await c.EliminarUbicacion(ubicacion.Id, " ");
            Assert.Contains("motivo", Error(c));

            await c.EliminarUbicacion(ubicacion.Id, "Ya no se usa");
            Assert.Single(e.Db.Solicitudes);
            Assert.Contains("Requiere aprobación", Exito(c));

            await c.EliminarUbicacion(ubicacion.Id, "Otra vez");
            Assert.Single(e.Db.Solicitudes);
            Assert.NotNull(Error(c));
        }
    }
}
