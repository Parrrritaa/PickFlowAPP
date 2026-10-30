using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PickingApp.Controllers;
using PickingApp.Models;
using PickingApp.Services;

namespace PickingApp.Tests
{
    // Filtro central por bodega (AlcanceBodega): lo de otra bodega no aparece en listas y por id responde 404.
    public class AlcanceBodegaTests
    {
        private sealed class SinAuditoria : IAuditService
        {
            public Task RegistrarAsync(string accion, string modulo, string detalle) => Task.CompletedTask;
            public Task LogAsync(string usuario, string rol, string accion, string modulo, string detalle, string? ip = null) => Task.CompletedTask;
        }

        private sealed class SinTempData : ITempDataProvider
        {
            public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
            public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
        }

        private static Usuario Usuario(Escenario e, int rolId, Bodega? bodega)
        {
            var usuario = new Usuario
            {
                NombreCompleto = "Usuario de prueba", Correo = $"{Guid.NewGuid():N}@prueba.com",
                PasswordHash = "hash", RolId = rolId, Bodega = bodega
            };
            e.Db.Usuarios.Add(usuario);
            e.Db.SaveChanges();
            e.Db.Entry(usuario).Reference(u => u.Rol).Load();
            return usuario;
        }

        private static Bodega Principal(Escenario e) => e.Db.Bodegas.Single(b => b.Codigo == "BOD-01");

        private static T Como<T>(T controlador, Usuario quien) where T : Controller
        {
            var http = new DefaultHttpContext { User = SesionUsuario.CrearPrincipal(quien) };
            controlador.ControllerContext = new ControllerContext { HttpContext = http };
            controlador.TempData = new TempDataDictionary(http, new SinTempData());
            return controlador;
        }

        private static PedidosController Pedidos(Escenario e, Usuario quien) =>
            Como(new PedidosController(e.Db, new ExcelService(e.Db, NullLogger<ExcelService>.Instance),
                e.Motor(), new SinAuditoria()), quien);

        [Fact]
        public async Task ElSupervisorSoloVeLoDeSuBodegaYElAdministradorTodo()
        {
            using var e = new Escenario();
            var ubicacion = e.Ubicacion(Escenario.Mediano);
            e.Pedido(PedidoEstados.Pendiente, (ubicacion, 1));
            var empleado = e.Empleado();
            empleado.Bodega = Principal(e);
            var sur = new Bodega { Codigo = "BOD-91", Nombre = "Sur" };
            var delSur = SesionUsuario.CrearPrincipal(Usuario(e, Roles.IdSupervisor, sur));
            var deLaPrincipal = SesionUsuario.CrearPrincipal(Usuario(e, Roles.IdAuxiliar, Principal(e)));
            var admin = SesionUsuario.CrearPrincipal(Usuario(e, Roles.IdAdministrador, null));

            Assert.Equal(0, await e.Db.Pedidos.DelAlcance(delSur).CountAsync());
            Assert.Equal(0, await e.Db.Ubicaciones.DelAlcance(delSur).CountAsync());
            Assert.Equal(0, await e.Db.Empleados.DelAlcance(delSur).CountAsync());
            Assert.Equal(new[] { "Sur" }, await e.Db.Bodegas.DelAlcance(delSur).Select(b => b.Nombre).ToListAsync());

            Assert.Equal(1, await e.Db.Pedidos.DelAlcance(deLaPrincipal).CountAsync());
            Assert.Equal(1, await e.Db.Empleados.DelAlcance(deLaPrincipal).CountAsync());

            Assert.Equal(2, await e.Db.Bodegas.DelAlcance(admin).CountAsync());
            Assert.Equal(1, await e.Db.Pedidos.DelAlcance(admin).CountAsync());
        }

        [Fact]
        public async Task UnSupervisorSinBodegaEnLaCookieNoVeNada()
        {
            using var e = new Escenario();
            var ubicacion = e.Ubicacion(Escenario.Mediano);
            e.Pedido(PedidoEstados.Pendiente, (ubicacion, 1));
            var sinBodega = SesionUsuario.CrearPrincipal(Usuario(e, Roles.IdSupervisor, null));

            Assert.Equal(-1, sinBodega.BodegaVisible());
            Assert.Equal(0, await e.Db.Pedidos.DelAlcance(sinBodega).CountAsync());
            // Los empleados aún sin bodega tampoco: el -1 no coincide con un BodegaId nulo
            e.Empleado();
            Assert.Equal(0, await e.Db.Empleados.DelAlcance(sinBodega).CountAsync());
        }

        [Fact]
        public async Task UnPedidoOUnaUbicacionDeOtraBodegaResponden404()
        {
            using var e = new Escenario();
            var ubicacion = e.Ubicacion(Escenario.Mediano);
            var pedido = e.Pedido(PedidoEstados.Pendiente, (ubicacion, 1));
            var delSur = Usuario(e, Roles.IdSupervisor, new Bodega { Codigo = "BOD-91", Nombre = "Sur" });
            var deLaPrincipal = Usuario(e, Roles.IdSupervisor, Principal(e));

            Assert.IsType<NotFoundResult>(await Pedidos(e, delSur).Detalle(pedido.Id));
            Assert.IsType<NotFoundResult>(await Pedidos(e, delSur).Asignar(pedido.Id));
            Assert.IsType<NotFoundResult>(await Como(new BodegasController(e.Db, new SinAuditoria()), delSur).EditarUbicacion(ubicacion.Id));
            Assert.IsType<NotFoundResult>(await Como(new BodegasController(e.Db, new SinAuditoria()), delSur).Detalle(Principal(e).Id, null, null));

            Assert.IsType<ViewResult>(await Pedidos(e, deLaPrincipal).Detalle(pedido.Id));
        }

        [Fact]
        public async Task LaSolicitudQuedaEnLaBodegaDeQuienLaCrea()
        {
            using var e = new Escenario();
            var auxiliar = Usuario(e, Roles.IdAuxiliar, Principal(e));
            var delSur = Usuario(e, Roles.IdSupervisor, new Bodega { Codigo = "BOD-91", Nombre = "Sur" });
            var deLaPrincipal = Usuario(e, Roles.IdSupervisor, Principal(e));

            var solicitud = await FlujoSolicitudes.CrearAsync(e.Db, SesionUsuario.CrearPrincipal(auxiliar), TiposSolicitud.RegistroEmpleado,
                new DatosEmpleadoSolicitud { Identificacion = "123", NombreCompleto = "Nuevo" }, "Solicitud", "el registro", TiposNotificacion.Info);

            Assert.Equal(Principal(e).Id, solicitud.BodegaId);
            SolicitudesController Solicitudes(Usuario quien) =>
                Como(new SolicitudesController(e.Db, new SinAuditoria(), NullLogger<SolicitudesController>.Instance), quien);
            Assert.IsType<NotFoundResult>(await Solicitudes(delSur).Revisar(solicitud.Id));
            Assert.IsType<ViewResult>(await Solicitudes(deLaPrincipal).Revisar(solicitud.Id));
        }
    }
}
