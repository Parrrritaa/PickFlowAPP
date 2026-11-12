using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PickingApp.Controllers;
using PickingApp.Models;
using PickingApp.Models.ViewModels;
using PickingApp.Services;

namespace PickingApp.Tests
{
    // Solicitud de traslado del Supervisor y lo que queda en cada bodega después
    public class TrasladosTests
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

        private static Bodega Principal(Escenario e) => e.Db.Bodegas.Single(b => b.Codigo == "BOD-01");

        private static Bodega Sur(Escenario e)
        {
            var sur = new Bodega { Codigo = "BOD-91", Nombre = "Sur" };
            e.Db.Bodegas.Add(sur);
            e.Db.SaveChanges();
            return sur;
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

        private static T Como<T>(T controlador, Usuario quien) where T : Controller
        {
            var http = new DefaultHttpContext { User = SesionUsuario.CrearPrincipal(quien) };
            controlador.ControllerContext = new ControllerContext { HttpContext = http };
            controlador.TempData = new TempDataDictionary(http, new SinTempData());
            return controlador;
        }

        private static SolicitudesController Solicitudes(Escenario e, Usuario quien) =>
            Como(new SolicitudesController(e.Db, new SinAuditoria(), NullLogger<SolicitudesController>.Instance), quien);

        private static EmpleadosController Empleados(Escenario e, Usuario quien) =>
            Como(new EmpleadosController(e.Db, new SinAuditoria(), e.Motor()), quien);

        // El Supervisor solicita y el Administrador aprueba
        private static async Task TrasladarAsync(Escenario e, int empleadoId, Usuario supervisor, Usuario admin, int destino)
        {
            await Solicitudes(e, supervisor).SolicitarTraslado(true, empleadoId, destino, "Refuerzo de personal");
            var solicitud = e.Db.Solicitudes.Single(s => s.Estado == EstadosSolicitud.Pendiente && s.TipoSolicitudId == TiposSolicitud.Traslado);
            await Solicitudes(e, admin).Aprobar(solicitud.Id, null, null);
        }

        [Fact]
        public async Task AlAprobarseElEmpleadoCambiaDeBodegaYQuedaElRegistro()
        {
            using var e = new Escenario();
            var empleado = e.Empleado();
            var sur = Sur(e);
            var admin = Usuario(e, Roles.IdAdministrador, null);
            var supervisor = Usuario(e, Roles.IdSupervisor, Principal(e));

            await Solicitudes(e, supervisor).SolicitarTraslado(true, empleado.Id, sur.Id, "Refuerzo de personal");

            Assert.Equal(Principal(e).Id, empleado.BodegaId);
            Assert.Contains(e.Db.Notificaciones, n => n.UsuarioDestinoId == admin.Id);
            var solicitud = e.Db.Solicitudes.Single();

            await Solicitudes(e, admin).Aprobar(solicitud.Id, null, null);

            Assert.Equal(EstadosSolicitud.Aprobada, solicitud.Estado);
            Assert.Equal(sur.Id, empleado.BodegaId);
            var registro = e.Db.TrasladosPersonal.Single();
            Assert.Equal((Principal(e).Id, sur.Id), (registro.BodegaOrigenId, registro.BodegaDestinoId));
        }

        [Fact]
        public async Task ElSupervisorNoPuedeAprobarNiPedirElTrasladoDeOtraBodega()
        {
            using var e = new Escenario();
            var empleado = e.Empleado();
            var sur = Sur(e);
            var propio = Usuario(e, Roles.IdSupervisor, Principal(e));
            var ajeno = Usuario(e, Roles.IdSupervisor, sur);

            Assert.IsType<NotFoundResult>(await Solicitudes(e, ajeno).SolicitarTraslado(true, empleado.Id, Principal(e).Id, "Motivo"));

            await Solicitudes(e, propio).SolicitarTraslado(true, empleado.Id, sur.Id, "Motivo");
            await Solicitudes(e, propio).Aprobar(e.Db.Solicitudes.Single().Id, null, null);

            Assert.Equal(EstadosSolicitud.Pendiente, e.Db.Solicitudes.Single().Estado);
            Assert.Equal(Principal(e).Id, empleado.BodegaId);
        }

        [Fact]
        public async Task NoSeSolicitaHaciaLaBodegaPropiaNiSinMotivoNiDosVeces()
        {
            using var e = new Escenario();
            var empleado = e.Empleado();
            var sur = Sur(e);
            var supervisor = Usuario(e, Roles.IdSupervisor, Principal(e));

            await Solicitudes(e, supervisor).SolicitarTraslado(true, empleado.Id, Principal(e).Id, "Motivo");
            await Solicitudes(e, supervisor).SolicitarTraslado(true, empleado.Id, sur.Id, "   ");
            Assert.Empty(e.Db.Solicitudes);

            await Solicitudes(e, supervisor).SolicitarTraslado(true, empleado.Id, sur.Id, "Motivo");
            await Solicitudes(e, supervisor).SolicitarTraslado(true, empleado.Id, sur.Id, "Motivo");
            Assert.Single(e.Db.Solicitudes);
        }

        [Fact]
        public async Task NoSeApruebaSiTomoUnPedidoMientrasEsperaba()
        {
            using var e = new Escenario();
            var empleado = e.Empleado();
            var sur = Sur(e);
            var admin = Usuario(e, Roles.IdAdministrador, null);
            var supervisor = Usuario(e, Roles.IdSupervisor, Principal(e));
            await Solicitudes(e, supervisor).SolicitarTraslado(true, empleado.Id, sur.Id, "Motivo");

            var pedido = e.Pedido(PedidoEstados.Asignado, (e.Ubicacion(Escenario.Pequeno), 1));
            pedido.Empleado = empleado;
            e.Db.SaveChanges();
            await Solicitudes(e, admin).Aprobar(e.Db.Solicitudes.Single().Id, null, null);

            Assert.Equal(EstadosSolicitud.Pendiente, e.Db.Solicitudes.Single().Estado);
            Assert.Equal(Principal(e).Id, empleado.BodegaId);
            Assert.Empty(e.Db.TrasladosPersonal);
        }

        [Fact]
        public async Task SeTrasladaUnAuxiliar()
        {
            using var e = new Escenario();
            var sur = Sur(e);
            var admin = Usuario(e, Roles.IdAdministrador, null);
            var supervisor = Usuario(e, Roles.IdSupervisor, Principal(e));
            var auxiliar = Usuario(e, Roles.IdAuxiliar, Principal(e));

            await Solicitudes(e, supervisor).SolicitarTraslado(false, auxiliar.Id, sur.Id, "Motivo");
            await Solicitudes(e, admin).Aprobar(e.Db.Solicitudes.Single().Id, null, null);

            Assert.Equal(sur.Id, auxiliar.BodegaId);
            Assert.Equal(auxiliar.Id, (await Traslados.AuxiliaresQueSalieronAsync(e.Db, Principal(e).Id)).Single().UsuarioId);
        }

        [Fact]
        public async Task CadaBodegaVeSoloSusPedidosYLaPrimeraNoPuedeActivarlo()
        {
            using var e = new Escenario();
            var empleado = e.Empleado();
            e.Completado(empleado, 10, 10);
            var sur = Sur(e);
            var admin = Usuario(e, Roles.IdAdministrador, null);
            var dePrincipal = Usuario(e, Roles.IdSupervisor, Principal(e));
            var delSur = Usuario(e, Roles.IdSupervisor, sur);

            await TrasladarAsync(e, empleado.Id, dePrincipal, admin, sur.Id);

            var enPrincipal = Assert.IsType<EmpleadosIndexViewModel>(
                Assert.IsType<ViewResult>(await Empleados(e, dePrincipal).Index(null)).Model);
            Assert.Empty(enPrincipal.Empleados);
            Assert.Equal(empleado.Id, enPrincipal.Trasladados.Single().EmpleadoId);

            var fichaPrincipal = Assert.IsType<EmpleadoHistorialViewModel>(
                Assert.IsType<ViewResult>(await Empleados(e, dePrincipal).Historial(empleado.Id, null)).Model);
            Assert.Equal(1, fichaPrincipal.TotalCompletados);
            Assert.NotNull(fichaPrincipal.SalidaDeEstaBodega);

            var fichaSur = Assert.IsType<EmpleadoHistorialViewModel>(
                Assert.IsType<ViewResult>(await Empleados(e, delSur).Historial(empleado.Id, null)).Model);
            Assert.Equal(0, fichaSur.TotalCompletados);
            Assert.Null(fichaSur.SalidaDeEstaBodega);
            Assert.Equal(2, fichaSur.Periodos.Count);

            // Desde la bodega que dejó no se activa, ni se edita, ni se pide otro traslado
            empleado.Activo = false;
            e.Db.SaveChanges();
            Assert.IsType<NotFoundResult>(await Empleados(e, dePrincipal).Inactivar(empleado.Id, "Motivo"));
            Assert.IsType<NotFoundResult>(await Empleados(e, dePrincipal).Editar(empleado.Id));
            Assert.IsType<NotFoundResult>(await Solicitudes(e, dePrincipal).SolicitarTraslado(true, empleado.Id, sur.Id, "Motivo"));
            Assert.False(empleado.Activo);
        }

        [Fact]
        public async Task AlRegresarQuedaActivoYRetomaSusPedidos()
        {
            using var e = new Escenario();
            var empleado = e.Empleado();
            e.Completado(empleado, 10, 10);
            var sur = Sur(e);
            var admin = Usuario(e, Roles.IdAdministrador, null);
            var dePrincipal = Usuario(e, Roles.IdSupervisor, Principal(e));
            var delSur = Usuario(e, Roles.IdSupervisor, sur);

            await TrasladarAsync(e, empleado.Id, dePrincipal, admin, sur.Id);
            empleado.Activo = false;
            e.Db.SaveChanges();
            await TrasladarAsync(e, empleado.Id, delSur, admin, Principal(e).Id);

            Assert.True(empleado.Activo);
            var lista = Assert.IsType<EmpleadosIndexViewModel>(
                Assert.IsType<ViewResult>(await Empleados(e, dePrincipal).Index(null)).Model);
            Assert.Equal(empleado.Id, lista.Empleados.Single().Id);
            Assert.Empty(lista.Trasladados);
            var ficha = Assert.IsType<EmpleadoHistorialViewModel>(
                Assert.IsType<ViewResult>(await Empleados(e, dePrincipal).Historial(empleado.Id, null)).Model);
            Assert.Equal(1, ficha.TotalCompletados);
            Assert.Null(ficha.SalidaDeEstaBodega);
        }

        [Fact]
        public async Task UnTrasladoAUnaBodegaNuevaNoReactivaAlEmpleado()
        {
            using var e = new Escenario();
            var empleado = e.Empleado(activo: false);
            var sur = Sur(e);

            await Traslados.CambiarBodegaAsync(e.Db, empleado, sur.Id);

            Assert.False(empleado.Activo);
        }
    }
}
