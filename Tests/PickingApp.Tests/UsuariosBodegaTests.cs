using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using PickingApp.Controllers;
using PickingApp.Models;
using PickingApp.Services;

namespace PickingApp.Tests
{
    // El Supervisor gestiona solo a los auxiliares de su bodega; rol y bodega los decide el Administrador.
    public class UsuariosBodegaTests
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

        private static UsuariosController Como(Escenario e, Usuario quien)
        {
            var http = new DefaultHttpContext { User = SesionUsuario.CrearPrincipal(quien) };
            return new UsuariosController(e.Db, new SinAuditoria())
            {
                ControllerContext = new ControllerContext { HttpContext = http },
                TempData = new TempDataDictionary(http, new SinTempData())
            };
        }

        private static Usuario Nuevo(int rolId, int? bodegaId) => new()
        {
            NombreCompleto = "Nuevo", Identificacion = "123", Correo = $"{Guid.NewGuid():N}@prueba.com",
            RolId = rolId, BodegaId = bodegaId, Estado = EstadosUsuario.Activo
        };

        [Fact]
        public async Task ElSupervisorSoloCreaAuxiliaresDeSuBodega()
        {
            using var e = new Escenario();
            var norte = new Bodega { Codigo = "BOD-90", Nombre = "Norte" };
            var sur = new Bodega { Codigo = "BOD-91", Nombre = "Sur" };
            var supervisor = Usuario(e, Roles.IdSupervisor, norte);
            e.Db.Bodegas.Add(sur);
            e.Db.SaveChanges();

            // Aunque el formulario manipulado pida Administrador en otra bodega
            var model = Nuevo(Roles.IdAdministrador, sur.Id);
            await Como(e, supervisor).Crear(model, "Clave123", "Clave123");

            var creado = e.Db.Usuarios.Single(u => u.Correo == model.Correo);
            Assert.Equal(Roles.IdAuxiliar, creado.RolId);
            Assert.Equal(norte.Id, creado.BodegaId);
        }

        [Fact]
        public async Task ElSupervisorNoVeUsuariosFueraDeSuAlcance()
        {
            using var e = new Escenario();
            var norte = new Bodega { Codigo = "BOD-90", Nombre = "Norte" };
            var sur = new Bodega { Codigo = "BOD-91", Nombre = "Sur" };
            var supervisor = Usuario(e, Roles.IdSupervisor, norte);
            var auxiliarDelSur = Usuario(e, Roles.IdAuxiliar, sur);
            var otroSupervisor = Usuario(e, Roles.IdSupervisor, norte);
            var controlador = Como(e, supervisor);

            Assert.IsType<NotFoundResult>(await controlador.Editar(auxiliarDelSur.Id));
            Assert.IsType<NotFoundResult>(await controlador.Editar(otroSupervisor.Id));
            Assert.IsType<NotFoundResult>(await controlador.Desbloquear(auxiliarDelSur.Id));
        }

        [Fact]
        public async Task ElSupervisorEditaSuAuxiliarSinCambiarleRolNiBodega()
        {
            using var e = new Escenario();
            var norte = new Bodega { Codigo = "BOD-90", Nombre = "Norte" };
            var sur = new Bodega { Codigo = "BOD-91", Nombre = "Sur" };
            var supervisor = Usuario(e, Roles.IdSupervisor, norte);
            var auxiliar = Usuario(e, Roles.IdAuxiliar, norte);
            e.Db.Bodegas.Add(sur);
            e.Db.SaveChanges();

            await Como(e, supervisor).Editar(auxiliar.Id, "Nombre nuevo", "456", Roles.IdSupervisor, EstadosUsuario.Inactivo, sur.Id);

            Assert.Equal("Nombre nuevo", auxiliar.NombreCompleto);
            Assert.Equal(EstadosUsuario.Inactivo, auxiliar.Estado);
            Assert.Equal(Roles.IdAuxiliar, auxiliar.RolId);
            Assert.Equal(norte.Id, auxiliar.BodegaId);
        }

        [Fact]
        public async Task ElAdministradorAsignaBodegaSalvoAOtroAdministrador()
        {
            using var e = new Escenario();
            var norte = new Bodega { Codigo = "BOD-90", Nombre = "Norte" };
            var inactiva = new Bodega { Codigo = "BOD-92", Nombre = "Cerrada", Activa = false };
            e.Db.Bodegas.AddRange(norte, inactiva);
            var admin = Usuario(e, Roles.IdAdministrador, null);

            var sinBodega = Nuevo(Roles.IdSupervisor, null);
            var controlador = Como(e, admin);
            await controlador.Crear(sinBodega, "Clave123", "Clave123");
            Assert.True(controlador.ModelState.ContainsKey("BodegaId"));

            controlador = Como(e, admin);
            await controlador.Crear(Nuevo(Roles.IdAuxiliar, inactiva.Id), "Clave123", "Clave123");
            Assert.True(controlador.ModelState.ContainsKey("BodegaId"));

            var otroAdmin = Nuevo(Roles.IdAdministrador, norte.Id);
            await Como(e, admin).Crear(otroAdmin, "Clave123", "Clave123");
            Assert.Null(e.Db.Usuarios.Single(u => u.Correo == otroAdmin.Correo).BodegaId);
        }

        // Caso real: un auxiliar quedó registrado con la cédula de su supervisor
        [Fact]
        public async Task DosUsuariosNoCompartenIdentificacion()
        {
            using var e = new Escenario();
            var norte = new Bodega { Codigo = "BOD-90", Nombre = "Norte" };
            var supervisor = Usuario(e, Roles.IdSupervisor, norte);
            var auxiliar = Usuario(e, Roles.IdAuxiliar, norte);
            supervisor.Identificacion = "1014861690";
            auxiliar.Identificacion = "555";
            e.Db.SaveChanges();

            var model = Nuevo(Roles.IdAuxiliar, norte.Id);
            model.Identificacion = " 1014861690 ";
            var controlador = Como(e, supervisor);
            await controlador.Crear(model, "Clave123", "Clave123");
            Assert.True(controlador.ModelState.ContainsKey("Identificacion"));
            Assert.False(e.Db.Usuarios.Any(u => u.Correo == model.Correo));

            await Como(e, supervisor).Editar(auxiliar.Id, "Auxiliar", "1014861690", auxiliar.RolId, EstadosUsuario.Activo, norte.Id);
            Assert.Equal("555", auxiliar.Identificacion);

            // Guardar sin cambiar la propia sí se puede
            await Como(e, supervisor).Editar(auxiliar.Id, "Auxiliar", "555", auxiliar.RolId, EstadosUsuario.Activo, norte.Id);
            Assert.Equal("Auxiliar", auxiliar.NombreCompleto);
        }
    }
}
