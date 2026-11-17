using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using PickingApp.Models;
using PickingApp.Services;

namespace PickingApp.Tests
{
    // Piezas comunes para probar controladores: sesión de un usuario, TempData y auditoría que no hacen nada
    public static class Ctl
    {
        public sealed class SinAuditoria : IAuditService
        {
            public List<string> Acciones { get; } = new();
            public Task RegistrarAsync(string accion, string modulo, string detalle) { Acciones.Add(accion); return Task.CompletedTask; }
            public Task LogAsync(string usuario, string rol, string accion, string modulo, string detalle, string? ip = null) { Acciones.Add(accion); return Task.CompletedTask; }
        }

        private sealed class SinTempData : ITempDataProvider
        {
            public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
            public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
        }

        public static Usuario Usuario(Escenario e, int rolId = Roles.IdAdministrador, Bodega? bodega = null)
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

        // Deja al controlador con la sesión de `quien` (o sin sesión) y un TempData en memoria
        public static T Con<T>(T controlador, Usuario? quien = null) where T : Controller
        {
            var http = new DefaultHttpContext();
            if (quien != null) http.User = SesionUsuario.CrearPrincipal(quien);
            controlador.ControllerContext = new ControllerContext { HttpContext = http };
            controlador.TempData = new TempDataDictionary(http, new SinTempData());
            return controlador;
        }
    }
}
