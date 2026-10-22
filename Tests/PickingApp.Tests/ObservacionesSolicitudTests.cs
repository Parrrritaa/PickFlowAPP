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
    // Catálogo de observaciones al responder solicitudes: filtrado por tipo y decisión, y la regla de "Otro"
    public class ObservacionesSolicitudTests
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

        private static Usuario Usuario(Escenario e, int rolId)
        {
            var usuario = new Usuario { NombreCompleto = "Usuario de prueba", Correo = $"{Guid.NewGuid():N}@prueba.com", PasswordHash = "hash", RolId = rolId };
            e.Db.Usuarios.Add(usuario);
            e.Db.SaveChanges();
            e.Db.Entry(usuario).Reference(u => u.Rol).Load();
            return usuario;
        }

        // Solicitud de registro de empleado pendiente y el controlador con sesión de Administrador
        private static (SolicitudesController Controlador, Solicitud Solicitud) Preparar(Escenario e)
        {
            var auxiliar = Usuario(e, Roles.IdAuxiliar);
            var administrador = Usuario(e, Roles.IdAdministrador);
            var solicitud = Solicitud.Nueva(TiposSolicitud.RegistroEmpleado, auxiliar.Id, new DatosEmpleadoSolicitud
            {
                Identificacion = "123456789",
                NombreCompleto = "Empleado solicitado",
                HorarioEntrada = new TimeSpan(8, 0, 0),
                HorarioSalida = new TimeSpan(16, 0, 0),
                FechaIngreso = new DateTime(2026, 1, 15)
            });
            e.Db.Solicitudes.Add(solicitud);
            e.Db.SaveChanges();

            var http = new DefaultHttpContext { User = SesionUsuario.CrearPrincipal(administrador) };
            var controlador = new SolicitudesController(e.Db, new SinAuditoria(), NullLogger<SolicitudesController>.Instance)
            {
                ControllerContext = new ControllerContext { HttpContext = http },
                TempData = new TempDataDictionary(http, new SinTempData())
            };
            return (controlador, solicitud);
        }

        private static int IdDe(string decision, int tipo) =>
            ObservacionesIniciales.Catalogo.First(o => o.Decision == decision && o.TipoSolicitudId == tipo).Id;

        [Fact]
        public void CadaTipoDeSolicitudTraeObservacionesParaAprobarYParaRechazar()
        {
            foreach (var tipo in TiposSolicitud.Catalogo)
            {
                Assert.Contains(ObservacionesIniciales.Catalogo, o => o.TipoSolicitudId == tipo.Id && o.Decision == DecisionesSolicitud.Aprobar);
                Assert.Contains(ObservacionesIniciales.Catalogo, o => o.TipoSolicitudId == tipo.Id && o.Decision == DecisionesSolicitud.Rechazar);
            }
            Assert.Equal(ObservacionesIniciales.Catalogo.Length, ObservacionesIniciales.Catalogo.Select(o => o.Id).Distinct().Count());
            Assert.Equal(ObservacionesIniciales.Catalogo.Length,
                ObservacionesIniciales.Catalogo.Select(o => (o.TipoSolicitudId, o.Decision, o.Texto)).Distinct().Count());
        }

        [Theory]
        [InlineData(".", false)]
        [InlineData("asdf", false)]
        [InlineData("1234567890", false)]
        [InlineData("..........ab", false)]
        [InlineData("Falta el documento de identidad", true)]
        public void ElTextoLibreDeOtroPideAlMenosDiezCaracteresConPalabras(string texto, bool valido)
        {
            Assert.Equal(valido, ObservacionSolicitud.TextoLibreValido(texto));
        }

        [Fact]
        public async Task RechazarConUnaObservacionDelCatalogoLaGuardaPorId()
        {
            using var e = new Escenario();
            var (controlador, solicitud) = Preparar(e);
            var id = IdDe(DecisionesSolicitud.Rechazar, TiposSolicitud.RegistroEmpleado);

            await controlador.Rechazar(solicitud.Id, id.ToString(), null);

            var guardada = await e.Db.Solicitudes.SingleAsync();
            Assert.Equal(EstadosSolicitud.Rechazada, guardada.Estado);
            Assert.Equal(id, guardada.ObservacionId);
            Assert.Null(guardada.ObservacionRespuesta);
            Assert.Contains(guardada.TextoObservacion!, (await e.Db.Notificaciones.SingleAsync()).Mensaje);
        }

        [Theory]
        [InlineData(null)]                                   // sin observación
        [InlineData("otro")]                                 // "Otro" sin texto
        public async Task NoSeRechazaSinObservacion(string? observacion)
        {
            using var e = new Escenario();
            var (controlador, solicitud) = Preparar(e);

            await controlador.Rechazar(solicitud.Id, observacion, observacion == null ? null : ".");

            Assert.Equal(EstadosSolicitud.Pendiente, (await e.Db.Solicitudes.SingleAsync()).Estado);
        }

        [Fact]
        public async Task NoSeAceptaUnaObservacionDeOtroTipoNiDeOtraDecision()
        {
            using var e = new Escenario();
            var (controlador, solicitud) = Preparar(e);

            await controlador.Rechazar(solicitud.Id, IdDe(DecisionesSolicitud.Rechazar, TiposSolicitud.EliminacionUbicacion).ToString(), null);
            await controlador.Rechazar(solicitud.Id, IdDe(DecisionesSolicitud.Aprobar, TiposSolicitud.RegistroEmpleado).ToString(), null);
            await controlador.Aprobar(solicitud.Id, IdDe(DecisionesSolicitud.Rechazar, TiposSolicitud.RegistroEmpleado).ToString(), null);

            Assert.Equal(EstadosSolicitud.Pendiente, (await e.Db.Solicitudes.SingleAsync()).Estado);
            Assert.False(await e.Db.Empleados.AnyAsync());
        }

        [Fact]
        public async Task NoSeAceptaUnaObservacionDesactivada()
        {
            using var e = new Escenario();
            var (controlador, solicitud) = Preparar(e);
            var id = IdDe(DecisionesSolicitud.Rechazar, TiposSolicitud.RegistroEmpleado);
            (await e.Db.ObservacionesSolicitud.FindAsync(id))!.Activa = false;
            await e.Db.SaveChangesAsync();

            await controlador.Rechazar(solicitud.Id, id.ToString(), null);

            Assert.Equal(EstadosSolicitud.Pendiente, (await e.Db.Solicitudes.SingleAsync()).Estado);
        }

        [Fact]
        public async Task RechazarConOtroGuardaElTextoLibre()
        {
            using var e = new Escenario();
            var (controlador, solicitud) = Preparar(e);

            await controlador.Rechazar(solicitud.Id, ObservacionSolicitud.ValorOtro, "  El cargo solicitado no existe en la bodega  ");

            var guardada = await e.Db.Solicitudes.SingleAsync();
            Assert.Equal(EstadosSolicitud.Rechazada, guardada.Estado);
            Assert.Null(guardada.ObservacionId);
            Assert.Equal("El cargo solicitado no existe en la bodega", guardada.ObservacionRespuesta);
        }

        [Fact]
        public async Task AprobarSinObservacionEsValido()
        {
            using var e = new Escenario();
            var (controlador, solicitud) = Preparar(e);

            await controlador.Aprobar(solicitud.Id, "", null);

            var guardada = await e.Db.Solicitudes.SingleAsync();
            Assert.Equal(EstadosSolicitud.Aprobada, guardada.Estado);
            Assert.Null(guardada.TextoObservacion);
            Assert.True(await e.Db.Empleados.AnyAsync(x => x.Identificacion == "123456789"));
        }
    }
}
