using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using PickingApp.Controllers;
using PickingApp.Models;
using PickingApp.Services;

namespace PickingApp.Tests
{
    // Bodega del empleado: solo el Administrador la elige o la cambia; lo demás hereda la de quien registra.
    public class EmpleadosBodegaTests
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

        private static EmpleadosController Como(Escenario e, Usuario quien)
        {
            var http = new DefaultHttpContext { User = SesionUsuario.CrearPrincipal(quien) };
            return new EmpleadosController(e.Db, new SinAuditoria(), e.Motor())
            {
                ControllerContext = new ControllerContext { HttpContext = http },
                TempData = new TempDataDictionary(http, new SinTempData())
            };
        }

        private static Empleado Formulario(int? bodegaId, string identificacion = "998877") => new()
        {
            Identificacion = identificacion, NombreCompleto = "Empleado nuevo",
            HorarioEntrada = new TimeSpan(8, 0, 0), HorarioSalida = new TimeSpan(16, 0, 0),
            FechaIngreso = new DateTime(2026, 1, 15), BodegaId = bodegaId
        };

        [Fact]
        public async Task LoQueRegistraElSupervisorQuedaEnSuBodega()
        {
            using var e = new Escenario();
            var norte = new Bodega { Codigo = "BOD-90", Nombre = "Norte" };
            var sur = new Bodega { Codigo = "BOD-91", Nombre = "Sur" };
            e.Db.Bodegas.Add(sur);
            var supervisor = Usuario(e, Roles.IdSupervisor, norte);

            await Como(e, supervisor).Crear(Formulario(sur.Id));

            Assert.Equal(norte.Id, e.Db.Empleados.Single(x => x.Identificacion == "998877").BodegaId);
        }

        [Fact]
        public async Task ElRegistroSolicitadoPorUnAuxiliarSeApruebaEnSuBodega()
        {
            using var e = new Escenario();
            var norte = new Bodega { Codigo = "BOD-90", Nombre = "Norte" };
            var auxiliar = Usuario(e, Roles.IdAuxiliar, norte);

            await Como(e, auxiliar).Crear(Formulario(bodegaId: null));
            var solicitud = e.Db.Solicitudes.Single();
            await FlujoSolicitudes.AplicarAsync(e.Db, solicitud);
            await e.Db.SaveChangesAsync();

            Assert.Equal(norte.Id, e.Db.Empleados.Single(x => x.Identificacion == "998877").BodegaId);
        }

        [Fact]
        public async Task NoSeTrasladaUnEmpleadoConUnPedidoEnCurso()
        {
            using var e = new Escenario();
            var norte = new Bodega { Codigo = "BOD-90", Nombre = "Norte" };
            var sur = new Bodega { Codigo = "BOD-91", Nombre = "Sur" };
            e.Db.Bodegas.AddRange(norte, sur);
            var admin = Usuario(e, Roles.IdAdministrador, null);
            var empleado = e.Empleado();
            empleado.Bodega = norte;
            var pedido = e.Pedido(PedidoEstados.EnCurso.First());
            pedido.Empleado = empleado;
            e.Db.SaveChanges();

            var formulario = Formulario(sur.Id, empleado.Identificacion);
            formulario.Id = empleado.Id;
            var controlador = Como(e, admin);
            await controlador.Editar(formulario, motivo: null);

            Assert.Contains(controlador.ModelState["BodegaId"]!.Errors, x => x.ErrorMessage.Contains("en curso"));
            Assert.Equal(norte.Id, empleado.BodegaId);

            pedido.Estado = PedidoEstados.Completado;
            e.Db.SaveChanges();
            await Como(e, admin).Editar(formulario, motivo: null);
            Assert.Equal(sur.Id, empleado.BodegaId);
        }

        [Fact]
        public async Task UnaIdentificacionDeOtraBodegaSugiereElTraslado()
        {
            using var e = new Escenario();
            var norte = new Bodega { Codigo = "BOD-90", Nombre = "Norte" };
            var supervisor = Usuario(e, Roles.IdSupervisor, new Bodega { Codigo = "BOD-91", Nombre = "Sur" });
            var empleado = e.Empleado();
            empleado.Bodega = norte;
            e.Db.SaveChanges();

            var controlador = Como(e, supervisor);
            await controlador.Crear(Formulario(null, empleado.Identificacion));

            Assert.Contains(controlador.ModelState["Identificacion"]!.Errors, x => x.ErrorMessage.Contains("bodega Norte"));
        }
    }
}
