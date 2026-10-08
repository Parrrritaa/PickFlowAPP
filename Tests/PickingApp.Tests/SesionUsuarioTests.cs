using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using PickingApp.Models;
using PickingApp.Services;

namespace PickingApp.Tests
{
    // La cookie de sesión se compara con la base de datos en cada petición (SesionUsuario.ValidarAsync).
    public class SesionUsuarioTests
    {
        private static Usuario Usuario(Escenario e)
        {
            var usuario = new Usuario
            {
                NombreCompleto = "Usuario de prueba",
                Correo = $"{Guid.NewGuid():N}@prueba.com",
                PasswordHash = "hash",
                RolId = Roles.IdSupervisor
            };
            e.Db.Usuarios.Add(usuario);
            e.Db.SaveChanges();
            e.Db.Entry(usuario).Reference(u => u.Rol).Load();
            return usuario;
        }

        // Valida la cookie emitida con el estado que tenía el usuario al iniciar sesión
        private static async Task<CookieValidatePrincipalContext> ValidarAsync(Escenario e, ClaimsPrincipal cookie)
        {
            var servicios = new ServiceCollection();
            servicios.AddSingleton(e.Db);
            servicios.AddLogging();
            servicios.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie();
            var http = new DefaultHttpContext { RequestServices = servicios.BuildServiceProvider() };

            var contexto = new CookieValidatePrincipalContext(http,
                new AuthenticationScheme(CookieAuthenticationDefaults.AuthenticationScheme, null, typeof(CookieAuthenticationHandler)),
                new CookieAuthenticationOptions(),
                new AuthenticationTicket(cookie, CookieAuthenticationDefaults.AuthenticationScheme));
            await SesionUsuario.ValidarAsync(contexto);
            return contexto;
        }

        [Fact]
        public async Task UnaSesionSinCambiosSigueIgual()
        {
            using var e = new Escenario();
            var usuario = Usuario(e);
            var cookie = SesionUsuario.CrearPrincipal(usuario);

            var r = await ValidarAsync(e, cookie);

            Assert.Same(cookie, r.Principal);
            Assert.False(r.ShouldRenew);
        }

        [Theory]
        [InlineData(EstadosUsuario.Inactivo)]
        [InlineData(EstadosUsuario.Bloqueado)]
        public async Task UnaCuentaQueDejaDeEstarActivaPierdeLaSesion(string estado)
        {
            using var e = new Escenario();
            var usuario = Usuario(e);
            var cookie = SesionUsuario.CrearPrincipal(usuario);
            usuario.Estado = estado;
            e.Db.SaveChanges();

            var r = await ValidarAsync(e, cookie);

            Assert.Null(r.Principal);
        }

        [Fact]
        public async Task CambiarLaContrasenaCierraLasSesionesAbiertas()
        {
            using var e = new Escenario();
            var usuario = Usuario(e);
            var cookie = SesionUsuario.CrearPrincipal(usuario);
            usuario.EstablecerPasswordHash("otro hash");
            e.Db.SaveChanges();

            var r = await ValidarAsync(e, cookie);

            Assert.Null(r.Principal);
        }

        [Fact]
        public async Task UnCambioDeRolSeAplicaSinCerrarLaSesion()
        {
            using var e = new Escenario();
            var usuario = Usuario(e);
            var cookie = SesionUsuario.CrearPrincipal(usuario);
            usuario.RolId = Roles.IdAuxiliar;
            e.Db.SaveChanges();

            var r = await ValidarAsync(e, cookie);

            Assert.True(r.ShouldRenew);
            Assert.Equal(Roles.Auxiliar, r.Principal!.FindFirstValue(ClaimTypes.Role));
        }
    }
}
