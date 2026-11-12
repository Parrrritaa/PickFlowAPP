using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging.Abstractions;
using PickingApp.Controllers;
using PickingApp.Models;
using PickingApp.Models.ViewModels;
using PickingApp.Services;

namespace PickingApp.Tests
{
    // Lista de pedidos: las pestañas de estado cuentan con la búsqueda aplicada y la pestaña elegida filtra la lista
    public class PedidosListaTests
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

        [Fact]
        public async Task LasPestanasCuentanConLaBusquedaYFiltranPorEstado()
        {
            using var e = new Escenario();
            var ubicacion = e.Ubicacion(Escenario.Mediano);
            var ana = e.Empleado();
            ana.NombreCompleto = "Ana Ruiz";
            e.Pedido(PedidoEstados.Pendiente, (ubicacion, 1));
            var asignado = e.Pedido(PedidoEstados.Asignado, (ubicacion, 1));
            asignado.Empleado = ana;
            var completado = e.Pedido(PedidoEstados.Completado, (ubicacion, 1));
            completado.Empleado = ana;
            e.Db.SaveChanges();

            var admin = new Usuario { NombreCompleto = "Admin", Correo = "admin@prueba.com", PasswordHash = "hash", RolId = Roles.IdAdministrador };
            e.Db.Usuarios.Add(admin);
            e.Db.SaveChanges();
            e.Db.Entry(admin).Reference(u => u.Rol).Load();
            var http = new DefaultHttpContext { User = SesionUsuario.CrearPrincipal(admin) };
            var controlador = new PedidosController(e.Db, new ExcelService(e.Db, NullLogger<ExcelService>.Instance), e.Motor(), new SinAuditoria())
            {
                ControllerContext = new ControllerContext { HttpContext = http },
                TempData = new TempDataDictionary(http, new SinTempData())
            };

            // Buscar por el empleado deja fuera el pendiente; la pestaña Asignado muestra solo ese.
            // (La base en memoria distingue mayúsculas; MySQL no, por su intercalación)
            var vista = (PedidosIndexViewModel)((ViewResult)await controlador.Index(PedidoEstados.Asignado, null, null, "Ana",
                null, null, null, null, null, null)).Model!;

            Assert.Equal(new Dictionary<string, int> { [PedidoEstados.Asignado] = 1, [PedidoEstados.Completado] = 1 }, vista.ConteoPorEstado);
            Assert.Equal(asignado.Id, Assert.Single(vista.Pedidos).Id);
        }
    }
}
