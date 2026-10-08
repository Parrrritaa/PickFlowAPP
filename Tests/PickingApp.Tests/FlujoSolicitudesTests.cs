using Microsoft.EntityFrameworkCore;
using PickingApp.Models;
using PickingApp.Services;

namespace PickingApp.Tests
{
    // Crear y aplicar solicitudes (FlujoSolicitudes)
    public class FlujoSolicitudesTests
    {
        private static Usuario Usuario(Escenario e, int rolId)
        {
            var usuario = new Usuario { NombreCompleto = "Usuario de prueba", Correo = $"{Guid.NewGuid():N}@prueba.com", PasswordHash = "hash", RolId = rolId };
            e.Db.Usuarios.Add(usuario);
            e.Db.SaveChanges();
            e.Db.Entry(usuario).Reference(u => u.Rol).Load();
            return usuario;
        }

        private static DatosEmpleadoSolicitud Registro() => new()
        {
            Identificacion = "123456789",
            NombreCompleto = "Empleado solicitado",
            HorarioEntrada = new TimeSpan(8, 0, 0),
            HorarioSalida = new TimeSpan(16, 0, 0),
            FechaIngreso = new DateTime(2026, 1, 15)
        };

        [Fact]
        public async Task UnRegistroDeEmpleadoAvisaACadaSupervisor()
        {
            using var e = new Escenario();
            var auxiliar = Usuario(e, Roles.IdAuxiliar);
            var supervisores = new[] { Usuario(e, Roles.IdSupervisor), Usuario(e, Roles.IdSupervisor) };
            Usuario(e, Roles.IdAdministrador);

            var solicitud = await FlujoSolicitudes.CrearAsync(e.Db, SesionUsuario.CrearPrincipal(auxiliar), TiposSolicitud.RegistroEmpleado,
                Registro(), "Solicitud de Nuevo Empleado", "el registro del empleado Empleado solicitado", TiposNotificacion.Info);

            Assert.Equal(EstadosSolicitud.Pendiente, (await e.Db.Solicitudes.SingleAsync()).Estado);
            Assert.Equal(auxiliar.Id, solicitud.UsuarioSolicitanteId);
            var avisados = await e.Db.Notificaciones.Select(n => n.UsuarioDestinoId).OrderBy(id => id).ToListAsync();
            Assert.Equal(supervisores.Select(s => s.Id).OrderBy(id => id), avisados);
        }

        [Fact]
        public async Task AprobarUnRegistroCreaElEmpleado()
        {
            using var e = new Escenario();
            var solicitud = Solicitud.Nueva(TiposSolicitud.RegistroEmpleado, 1, Registro());

            await FlujoSolicitudes.AplicarAsync(e.Db, solicitud);
            await e.Db.SaveChangesAsync();

            Assert.True(await e.Db.Empleados.AnyAsync(x => x.Identificacion == "123456789" && x.NombreCompleto == "Empleado solicitado"));
        }

        [Fact]
        public async Task NoSeInactivaUnEmpleadoConUnPedidoEnCurso()
        {
            using var e = new Escenario();
            var empleado = e.Empleado();
            var pedido = e.Pedido(PedidoEstados.Asignado, (e.Ubicacion(Escenario.Pequeno), 1));
            pedido.Empleado = empleado;
            e.Db.SaveChanges();
            var solicitud = Solicitud.Nueva(TiposSolicitud.InactivacionEmpleado, 1, new SolicitudInactivacionEmpleado { EmpleadoId = empleado.Id, Motivo = "prueba" });

            var ex = await Assert.ThrowsAsync<ReglaNegocioException>(() => FlujoSolicitudes.AplicarAsync(e.Db, solicitud));

            Assert.Contains(pedido.CodigoPedido, ex.Message);
            Assert.True(empleado.Activo);
        }

        [Fact]
        public async Task NoSeCambiaElProductoDeUnaUbicacionConPedidosPendientes()
        {
            using var e = new Escenario();
            var ubicacion = e.Ubicacion(Escenario.Pequeno);
            var otra = e.Ubicacion(Escenario.Grande);
            e.Pedido(PedidoEstados.Pendiente, (ubicacion, 1));
            var cambios = new ListaCambios();
            cambios.Agregar(CamposUbicacion.Producto, ubicacion.Producto!.Codigo, otra.Producto!.Codigo);
            var solicitud = Solicitud.Nueva(TiposSolicitud.EdicionUbicacion, 1, new SolicitudEdicionUbicacion
            {
                UbicacionId = ubicacion.Id,
                Cambios = cambios,
                NuevosDatos = new DatosUbicacionSolicitud { ProductoId = otra.ProductoId, CodigoProducto = otra.Producto.Codigo, Activa = true }
            });

            await Assert.ThrowsAsync<ReglaNegocioException>(() => FlujoSolicitudes.AplicarAsync(e.Db, solicitud));
        }
    }
}
