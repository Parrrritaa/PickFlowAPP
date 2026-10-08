using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using PickingApp.Controllers;
using PickingApp.Models;
using PickingApp.Models.ViewModels;
using PickingApp.Services;

namespace PickingApp.Tests
{
    // Intentos fallidos de inicio de sesión (AccountController.Login); el máximo por defecto es 5
    public class LoginTests
    {
        private sealed class SinAuditoria : IAuditService
        {
            public Task RegistrarAsync(string accion, string modulo, string detalle) => Task.CompletedTask;
            public Task LogAsync(string usuario, string rol, string accion, string modulo, string detalle, string? ip = null) => Task.CompletedTask;
        }

        private sealed class SinCorreo : IEmailService
        {
            public string? UltimoCodigo { get; private set; }

            public Task EnviarCodigoVerificacionAsync(string destinatarioEmail, string nombreDestinatario, string codigo)
            {
                UltimoCodigo = codigo;
                return Task.CompletedTask;
            }
        }

        // Token del formulario válido o caducado, según la prueba
        private sealed class Antifalsificacion : IAntiforgery
        {
            private readonly bool _valido;
            public Antifalsificacion(bool valido = true) => _valido = valido;
            public AntiforgeryTokenSet GetAndStoreTokens(HttpContext httpContext) => new("nuevo", "cookie", "__RequestVerificationToken", null);
            public AntiforgeryTokenSet GetTokens(HttpContext httpContext) => GetAndStoreTokens(httpContext);
            public Task<bool> IsRequestValidAsync(HttpContext httpContext) => Task.FromResult(_valido);
            public Task ValidateRequestAsync(HttpContext httpContext) => Task.CompletedTask;
            public void SetCookieTokenAndHeader(HttpContext httpContext) { }
        }

        private sealed class SinTempData : ITempDataProvider
        {
            public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
            public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
        }

        [Fact]
        public async Task ElCodigoDeRecuperacionSeGuardaComoHashYSirveUnaVez()
        {
            using var e = new Escenario();
            var usuario = Usuario(e, Roles.IdAuxiliar);
            var correo = new SinCorreo();
            var http = new DefaultHttpContext();
            var controlador = new AccountController(e.Db, new SinAuditoria(), correo, new Antifalsificacion())
            {
                ControllerContext = new ControllerContext { HttpContext = http },
                TempData = new TempDataDictionary(http, new SinTempData())
            };

            await controlador.SolicitarRecuperacion(new SolicitarCodigoViewModel { Correo = usuario.Correo });
            var codigo = correo.UltimoCodigo!;

            Assert.NotEqual(codigo, usuario.CodigoVerificacionHash);
            Assert.True(BCrypt.Net.BCrypt.Verify(codigo, usuario.CodigoVerificacionHash));

            await controlador.VerificarCodigoRecuperacion(new VerificarCodigoRecuperacionViewModel
            {
                Correo = usuario.Correo, Codigo = codigo, NewPassword = "Nueva123", ConfirmPassword = "Nueva123"
            });

            Assert.True(BCrypt.Net.BCrypt.Verify("Nueva123", usuario.PasswordHash));
            Assert.Null(usuario.CodigoVerificacionHash);
        }

        private static Usuario Usuario(Escenario e, int rolId)
        {
            var usuario = new Usuario
            {
                NombreCompleto = "Usuario de prueba", Correo = $"{Guid.NewGuid():N}@prueba.com",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Correcta1", 4), RolId = rolId
            };
            e.Db.Usuarios.Add(usuario);
            e.Db.SaveChanges();
            return usuario;
        }

        // Errores generales que devolvería la pantalla de inicio de sesión
        private static async Task<string[]> FallarAsync(Escenario e, string correo, string password = "Incorrecta1")
        {
            var controlador = new AccountController(e.Db, new SinAuditoria(), new SinCorreo(), new Antifalsificacion())
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
            };
            await controlador.Login(new LoginViewModel { Correo = correo, Password = password });
            return controlador.ModelState[string.Empty]?.Errors.Select(x => x.ErrorMessage).ToArray() ?? Array.Empty<string>();
        }

        [Fact]
        public async Task CincoIntentosFallidosBloqueanLaCuenta()
        {
            using var e = new Escenario();
            var usuario = Usuario(e, Roles.IdSupervisor);

            for (var i = 0; i < 5; i++) await FallarAsync(e, usuario.Correo);

            Assert.Equal(EstadosUsuario.Bloqueado, usuario.Estado);
            Assert.Contains(await FallarAsync(e, usuario.Correo, "Correcta1"), m => m.Contains("bloqueada"));
        }

        [Fact]
        public async Task ElAdministradorNoSeBloquea()
        {
            using var e = new Escenario();
            var administrador = Usuario(e, Roles.IdAdministrador);

            for (var i = 0; i < 6; i++) await FallarAsync(e, administrador.Correo);

            Assert.Equal(EstadosUsuario.Activo, administrador.Estado);
        }

        [Fact]
        public async Task LaRespuestaNoRevelaSiLaCuentaExisteNiSuEstado()
        {
            using var e = new Escenario();
            var usuario = Usuario(e, Roles.IdSupervisor);

            var inexistente = await FallarAsync(e, "nadie@prueba.com");
            var existente = await FallarAsync(e, usuario.Correo);
            for (var i = 0; i < 4; i++) await FallarAsync(e, usuario.Correo);
            var bloqueada = await FallarAsync(e, usuario.Correo);

            Assert.Equal(new[] { "Credenciales de acceso inválidas." }, inexistente);
            Assert.Equal(inexistente, existente);
            Assert.Equal(inexistente, bloqueada);
        }

        [Fact]
        public async Task UnFormularioCaducadoSinSesionPideReintentarSinContarElIntento()
        {
            using var e = new Escenario();
            var usuario = Usuario(e, Roles.IdSupervisor);
            var controlador = new AccountController(e.Db, new SinAuditoria(), new SinCorreo(), new Antifalsificacion(valido: false))
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
            };
            controlador.ControllerContext.HttpContext.Request.Headers["X-Requested-With"] = "fetch";

            var r = (JsonResult)await controlador.Login(new LoginViewModel { Correo = usuario.Correo, Password = "Incorrecta1" });

            Assert.Contains("caducó", string.Join(" ", controlador.ModelState[string.Empty]!.Errors.Select(x => x.ErrorMessage)));
            Assert.Contains("nuevo", System.Text.Json.JsonSerializer.Serialize(r.Value));
            Assert.Equal(0, usuario.IntentosFallidos);
        }
    }
}
