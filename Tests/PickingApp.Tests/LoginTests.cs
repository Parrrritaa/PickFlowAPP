using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using PickingApp.Controllers;
using PickingApp.Models;
using PickingApp.Models.ViewModels;
using PickingApp.Services;
using Microsoft.Extensions.DependencyInjection;

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

            public Task EnviarCodigoVerificacionAsync(string destinatarioEmail, string nombreDestinatario, string codigo, PropositoCodigo proposito)
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
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Correcta1", 4), RolId = rolId,
                Bodega = rolId == Roles.IdAdministrador ? null : new Bodega { Codigo = "BOD-90", Nombre = "Norte" }
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
        public async Task SinUnaBodegaActivaNoEntraAunqueLaContrasenaSeaCorrecta()
        {
            using var e = new Escenario();
            var sinBodega = Usuario(e, Roles.IdSupervisor);
            sinBodega.Bodega = null;
            sinBodega.BodegaId = null;
            var bodegaInactiva = Usuario(e, Roles.IdAuxiliar);
            bodegaInactiva.Bodega!.Activa = false;
            e.Db.SaveChanges();

            Assert.Contains(await FallarAsync(e, sinBodega.Correo, "Correcta1"), m => m.Contains("no tiene una bodega"));
            Assert.Contains(await FallarAsync(e, bodegaInactiva.Correo, "Correcta1"), m => m.Contains("está inactiva"));
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
        public async Task ElPrimerInicioDeSesionPideElCodigoDelCorreo()
        {
            using var e = new Escenario();
            var usuario = Usuario(e, Roles.IdAuxiliar);
            usuario.CorreoVerificado = false;
            e.Db.SaveChanges();
            var correo = new SinCorreo();
            AccountController Controlador()
            {
                var http = new DefaultHttpContext
                {
                    RequestServices = new ServiceCollection().AddLogging().AddMvcCore().Services
                        .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie().Services.BuildServiceProvider()
                };
                return new AccountController(e.Db, new SinAuditoria(), correo, new Antifalsificacion())
                {
                    ControllerContext = new ControllerContext { HttpContext = http, RouteData = new Microsoft.AspNetCore.Routing.RouteData() },
                    TempData = new TempDataDictionary(http, new SinTempData())
                };
            }
            var navegador = Controlador();

            var login = (RedirectToActionResult)await navegador.Login(new LoginViewModel { Correo = usuario.Correo, Password = "Correcta1" });
            Assert.Equal(nameof(AccountController.VerificarCorreo), login.ActionName);
            var codigo = correo.UltimoCodigo!;

            // Sin haber escrito la contraseña en ese navegador, ni el código correcto sirve
            var otro = (RedirectToActionResult)await Controlador().VerificarCorreo(new VerificarCorreoViewModel { Codigo = codigo });
            Assert.Equal(nameof(AccountController.Login), otro.ActionName);

            await navegador.VerificarCorreo(new VerificarCorreoViewModel { Codigo = codigo == "000000" ? "111111" : "000000" });
            Assert.False(usuario.CorreoVerificado);
            navegador.ModelState.Clear(); // en la app cada petición trae un controlador nuevo

            var r =(RedirectToActionResult)await navegador.VerificarCorreo(new VerificarCorreoViewModel { Codigo = codigo });
            Assert.Equal("Home", r.ControllerName);
            Assert.True(usuario.CorreoVerificado);
            Assert.Null(usuario.CodigoVerificacionHash);
        }

        [Fact]
        public async Task UnCorreoSinVerificarNoRecibeCodigoDeRecuperacion()
        {
            using var e = new Escenario();
            var usuario = Usuario(e, Roles.IdAuxiliar);
            usuario.CorreoVerificado = false;
            e.Db.SaveChanges();
            var correo = new SinCorreo();
            var controlador = new AccountController(e.Db, new SinAuditoria(), correo, new Antifalsificacion());

            var r = (RedirectToActionResult)await controlador.SolicitarRecuperacion(new SolicitarCodigoViewModel { Correo = usuario.Correo });

            Assert.Equal("VerificarCodigoRecuperacion", r.ActionName);
            Assert.Null(correo.UltimoCodigo);
            Assert.Null(usuario.CodigoVerificacionHash);
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
