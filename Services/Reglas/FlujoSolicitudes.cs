using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PickingApp.Data;
using PickingApp.Models;

namespace PickingApp.Services
{
    // Flujo de aprobación: lo que un rol no puede hacer directamente queda como Solicitud; quien la revisa la aprueba
    // (y entonces se aplica el cambio) o la rechaza (SolicitudesController).
    public static class FlujoSolicitudes
    {
        // CU-08: el Supervisor solo revisa, aprueba o rechaza registros de empleados; el resto es del Administrador
        public const int TipoGestionableSupervisor = TiposSolicitud.RegistroEmpleado;

        // Guarda la solicitud y avisa a quien la revisa. queSolicito completa "El auxiliar Ana Díaz solicitó ...".
        public static async Task<Solicitud> CrearAsync(ApplicationDbContext context, ClaimsPrincipal solicitante, int tipo, object datos,
            string tituloAviso, string queSolicito, string tipoAviso)
        {
            var solicitud = Solicitud.Nueva(tipo, solicitante.IdUsuario(), datos);
            context.Solicitudes.Add(solicitud);
            await Avisos.AlRolAsync(context, tipo == TipoGestionableSupervisor ? Roles.IdSupervisor : Roles.IdAdministrador,
                tituloAviso, $"El {solicitante.DescripcionParaNotificacion()} solicitó {queSolicito}.", tipoAviso, "/Solicitudes/Index");
            await context.SaveChangesAsync();
            return solicitud;
        }

        // Aplica los cambios de la solicitud (solo los agrega al contexto; el SaveChanges lo hace quien aprueba).
        // Las reglas se validan otra vez: entre la creación de la solicitud y su aprobación otro registro pudo tomar
        // la identificación, el correo o el código, cambiar la jornada máxima o dejar un pedido en curso.
        public static Task AplicarAsync(ApplicationDbContext context, Solicitud solicitud) => solicitud.TipoSolicitudId switch
        {
            TiposSolicitud.RegistroEmpleado => RegistrarEmpleadoAsync(context, solicitud.LeerDatos<DatosEmpleadoSolicitud>()),
            TiposSolicitud.EdicionEmpleado => EditarEmpleadoAsync(context, solicitud.LeerDatos<SolicitudEdicionEmpleado>()),
            TiposSolicitud.InactivacionEmpleado => InactivarEmpleadoAsync(context, solicitud.LeerDatos<SolicitudInactivacionEmpleado>()),
            TiposSolicitud.EdicionUbicacion => EditarUbicacionAsync(context, solicitud.LeerDatos<SolicitudEdicionUbicacion>()),
            TiposSolicitud.EliminacionUbicacion => EliminarUbicacionAsync(context, solicitud.LeerDatos<SolicitudEliminacionUbicacion>()),
            _ => throw new ReglaNegocioException($"No se puede aprobar: tipo de solicitud desconocido ({solicitud.TipoSolicitudId}).")
        };

        private static async Task RegistrarEmpleadoAsync(ApplicationDbContext context, DatosEmpleadoSolicitud datos)
        {
            var identificacion = datos.Identificacion.Trim();
            var correo = ReglasEmpleado.NormalizarCorreo(datos.Correo);
            await ValidarReglasEmpleadoAsync(context, identificacion, correo, datos.HorarioEntrada, datos.HorarioSalida, empleadoId: null);

            context.Empleados.Add(new Empleado
            {
                Identificacion = identificacion,
                NombreCompleto = datos.NombreCompleto,
                Correo = correo,
                HorarioEntrada = datos.HorarioEntrada,
                HorarioSalida = datos.HorarioSalida,
                FechaIngreso = datos.FechaIngreso,
                BodegaId = datos.BodegaId
            });
        }

        // Solo se aplican los campos que la solicitud cambia: si otro campo se editó mientras esperaba,
        // aprobarla no lo devuelve al valor de antes
        private static async Task EditarEmpleadoAsync(ApplicationDbContext context, SolicitudEdicionEmpleado solicitud)
        {
            var datos = solicitud.NuevosDatos;
            var empleado = await context.Empleados.FindAsync(datos.Id)
                ?? throw new ReglaNegocioException("No se puede aprobar: el empleado ya no existe. Rechace la solicitud.");

            var campos = solicitud.Cambios.Select(c => c.Campo).ToHashSet();
            var correo = campos.Contains(CamposEmpleado.Correo) ? ReglasEmpleado.NormalizarCorreo(datos.Correo) : empleado.Correo;
            var entrada = campos.Contains(CamposEmpleado.HoraEntrada) ? datos.HorarioEntrada : empleado.HorarioEntrada;
            var salida = campos.Contains(CamposEmpleado.HoraSalida) ? datos.HorarioSalida : empleado.HorarioSalida;
            await ValidarReglasEmpleadoAsync(context, empleado.Identificacion, correo, entrada, salida, empleado.Id);

            if (campos.Contains(CamposEmpleado.Nombre)) empleado.NombreCompleto = datos.NombreCompleto;
            if (campos.Contains(CamposEmpleado.FechaIngreso)) empleado.FechaIngreso = datos.FechaIngreso;
            empleado.Correo = correo;
            empleado.HorarioEntrada = entrada;
            empleado.HorarioSalida = salida;
        }

        private static async Task InactivarEmpleadoAsync(ApplicationDbContext context, SolicitudInactivacionEmpleado datos)
        {
            var empleado = await context.Empleados.FindAsync(datos.EmpleadoId)
                ?? throw new ReglaNegocioException("No se puede aprobar: el empleado ya no existe. Rechace la solicitud.");

            // El empleado pudo tomar un pedido entre la creación de la solicitud y su aprobación
            var pedidoEnCurso = await context.Pedidos
                .Where(p => p.EmpleadoId == empleado.Id && PedidoEstados.EnCurso.Contains(p.Estado))
                .Select(p => p.CodigoPedido)
                .FirstOrDefaultAsync();
            if (pedidoEnCurso != null)
            {
                throw new ReglaNegocioException($"No se puede aprobar: el empleado '{empleado.NombreCompleto}' tiene el pedido {pedidoEnCurso} en curso y no es posible inactivarlo. Reasigne, complete o cancele el pedido primero; la solicitud sigue pendiente.");
            }
            empleado.Activo = false;
        }

        private static async Task EditarUbicacionAsync(ApplicationDbContext context, SolicitudEdicionUbicacion datos)
        {
            var ubicacion = await context.Ubicaciones
                .Include(u => u.Producto)
                .FirstOrDefaultAsync(u => u.Id == datos.UbicacionId)
                ?? throw new ReglaNegocioException("No se puede aprobar: la ubicación ya no existe. Rechace la solicitud.");

            // Solo se aplican los campos que la solicitud cambia (igual que en la edición de empleados)
            var nuevos = datos.NuevosDatos;
            var campos = datos.Cambios.Select(c => c.Campo).ToHashSet();

            // Pudieron llegar pedidos a la ubicación mientras la solicitud esperaba
            var impedimento = await ReglasUbicacion.ValidarCambioAsync(context, ubicacion,
                cambiaProducto: campos.Contains(CamposUbicacion.Producto) && (nuevos.ProductoNuevo || nuevos.ProductoId != ubicacion.ProductoId),
                quedaActiva: campos.Contains(CamposUbicacion.Estado) ? nuevos.Activa : ubicacion.Activa);
            if (impedimento != null)
            {
                throw new ReglaNegocioException($"{impedimento} La solicitud sigue pendiente.");
            }

            if (campos.Contains(CamposUbicacion.Codigo))
            {
                var duplicada = await context.Ubicaciones.AnyAsync(u =>
                    u.BodegaId == ubicacion.BodegaId && u.CodigoUbicacion == nuevos.CodigoUbicacion && u.Id != ubicacion.Id);
                if (duplicada)
                {
                    throw new ReglaNegocioException($"No se puede aprobar: ya existe otra ubicación con el código '{nuevos.CodigoUbicacion}' en esta bodega. La solicitud sigue pendiente.");
                }
                ubicacion.Pasillo = nuevos.Pasillo;
                ubicacion.Estante = nuevos.Estante;
                ubicacion.Nivel = nuevos.Nivel;
                ubicacion.CodigoUbicacion = nuevos.CodigoUbicacion;
            }
            if (campos.Contains(CamposUbicacion.Estado))
            {
                ubicacion.Activa = nuevos.Activa;
            }
            // La ubicación toma un producto existente sin modificarlo, o registra el producto nuevo propuesto
            if (campos.Contains(CamposUbicacion.Producto))
            {
                ubicacion.Producto = await ProductoDeSolicitudAsync(context, nuevos);
            }
        }

        private static async Task<Producto> ProductoDeSolicitudAsync(ApplicationDbContext context, DatosUbicacionSolicitud nuevos)
        {
            if (nuevos.ProductoId is int productoId)
            {
                return await context.Productos.FindAsync(productoId)
                    ?? throw new ReglaNegocioException("No se puede aprobar: el producto elegido ya no existe. Rechace la solicitud.");
            }

            var existente = await context.Productos
                .FirstOrDefaultAsync(p => p.Codigo.ToUpper() == nuevos.CodigoProducto.ToUpper());
            if (existente != null)
            {
                // Otro registro tomó el código mientras la solicitud esperaba
                if (nuevos.ProductoNuevo)
                {
                    throw new ReglaNegocioException($"No se puede aprobar: el producto {existente.Codigo} ya existe ({existente.Nombre}). Rechace la solicitud para que se elija como producto existente.");
                }
                // Solicitud anterior a esta opción: se usa el producto con ese código tal como está
                return existente;
            }

            // Si la categoría fue eliminada mientras tanto, el producto queda sin clasificar
            var categoriaId = nuevos.CategoriaProductoId;
            if (categoriaId.HasValue && !await context.CategoriasProducto.AnyAsync(c => c.Id == categoriaId.Value))
            {
                categoriaId = null;
            }

            var producto = new Producto
            {
                Codigo = nuevos.CodigoProducto,
                Nombre = nuevos.NombreProducto ?? nuevos.CodigoProducto,
                CategoriaProductoId = categoriaId,
                UnidadesPorCaja = Math.Max(1, nuevos.UnidadesPorCaja ?? 1)
            };
            context.Productos.Add(producto);
            return producto;
        }

        private static async Task EliminarUbicacionAsync(ApplicationDbContext context, SolicitudEliminacionUbicacion datos)
        {
            var ubicacion = await context.Ubicaciones.FindAsync(datos.UbicacionId)
                ?? throw new ReglaNegocioException("No se puede aprobar: la ubicación ya no existe. Rechace la solicitud.");

            // Pudo recibir pedidos después de creada la solicitud
            if (await context.DetallesPedido.AnyAsync(d => d.UbicacionId == ubicacion.Id))
            {
                throw new ReglaNegocioException($"No se puede aprobar: la ubicación '{ubicacion.CodigoUbicacion}' tiene pedidos asociados. Rechace la solicitud.");
            }
            context.Ubicaciones.Remove(ubicacion);
        }

        private static async Task ValidarReglasEmpleadoAsync(ApplicationDbContext context, string identificacion, string? correo,
            TimeSpan entrada, TimeSpan salida, int? empleadoId)
        {
            var error = await ReglasEmpleado.ValidarAsync(context, identificacion, correo, entrada, salida, empleadoId);
            if (error != null)
            {
                throw new ReglaNegocioException($"No se puede aprobar: {error.Mensaje} La solicitud sigue pendiente.");
            }
        }
    }

    // Una regla de negocio impide aplicar la solicitud; quien aprueba muestra el mensaje y la solicitud sigue pendiente
    public sealed class ReglaNegocioException : Exception
    {
        public ReglaNegocioException(string message) : base(message) { }
    }
}
