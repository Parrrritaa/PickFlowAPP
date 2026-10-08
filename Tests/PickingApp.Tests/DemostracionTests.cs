using Microsoft.EntityFrameworkCore;
using PickingApp.Models;
using PickingApp.Services;

namespace PickingApp.Tests
{
    public class DemostracionTests
    {
        [Fact]
        public void ElLogDeLaDemostracionSeDistingueDelReal()
        {
            var supervisor = new Usuario { Id = 2, Correo = "supervisor@prueba.com" };
            var sim = new SimulacionDemostracion(DateTime.Now, new Random(1), new List<Empleado>(), 0, supervisor, supervisor, new[] { supervisor });

            sim.Log(DateTime.Now, supervisor, "Asignar Pedido", "Pedido PED-1 asignado.");

            Assert.StartsWith("[Demostración] ", sim.Logs.Single().Detalle);
        }

        // La demostración completa: cada pedido se completa una sola vez en el LOG y todo lo simulado va marcado
        [Fact]
        public async Task LaDemostracionDejaUnLogCoherente()
        {
            using var e = new Escenario();
            for (var i = 0; i < 8; i++) e.Ubicacion(Escenario.Grande);
            for (var i = 0; i < 4; i++) e.Empleado(entrada: TimeSpan.Zero, salida: new TimeSpan(23, 59, 0));
            Usuario Nuevo(int rolId)
            {
                var u = new Usuario { NombreCompleto = $"Usuario {rolId}", Correo = $"{Guid.NewGuid():N}@prueba.com", PasswordHash = "hash", RolId = rolId };
                e.Db.Usuarios.Add(u);
                e.Db.SaveChanges();
                return u;
            }
            var administrador = Nuevo(Roles.IdAdministrador);
            Nuevo(Roles.IdSupervisor);
            Nuevo(Roles.IdAuxiliar);

            await new DatosDemostracion(e.Db, e.Motor()).GenerarAsync(administrador.Id);

            var logs = await e.Db.LogsSistema.ToListAsync();
            Assert.NotEmpty(logs);
            Assert.All(logs, l => Assert.StartsWith("[Demostración] ", l.Detalle));
            var completadosDosVeces = logs.Where(l => l.Accion == AccionesLog.CompletarPedido)
                .GroupBy(l => l.Detalle.Split(' ')[2])
                .Where(g => g.Count() > 1)
                .Select(g => g.Key);
            Assert.Empty(completadosDosVeces);
        }
    }
}
