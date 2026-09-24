using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickingApp.Data;
using PickingApp.Models;
using PickingApp.Services;

namespace PickingApp.Controllers
{
    [Authorize]
    public class SolicitudesController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IAuditService _auditService;

        public SolicitudesController(ApplicationDbContext context, IAuditService auditService)
        {
            _context = context;
            _auditService = auditService;
        }

        // Bandeja de aprobación para Administrador y Supervisor
        [Authorize(Roles = Roles.AdministradorOSupervisor)]
        public async Task<IActionResult> Index(string? estado = null)
        {
            var userRole = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role)?.Value;

            var query = _context.Solicitudes
                .Include(s => s.UsuarioSolicitante)
                .Include(s => s.UsuarioResponde)
                .AsQueryable();

            // Supervisor solo puede aprobar/rechazar solicitudes de Registro de Empleado (CU-08)
            if (userRole == Roles.Supervisor)
            {
                query = query.Where(s => s.TipoSolicitud == TipoGestionableSupervisor);
            }

            if (!string.IsNullOrEmpty(estado))
            {
                query = query.Where(s => s.Estado == estado);
            }
            else
            {
                // Por defecto mostrar pendientes primero
                query = query.OrderByDescending(s => s.Estado == EstadosSolicitud.Pendiente)
                             .ThenByDescending(s => s.FechaSolicitud);
            }

            var solicitudes = await query.ToListAsync();
            ViewBag.FiltroEstado = estado;
            return View(solicitudes);
        }

        // CU-17: Consultar estado de solicitudes propias (Auxiliar y Supervisor)
        public async Task<IActionResult> MisSolicitudes()
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            var misSolicitudes = await _context.Solicitudes
                .Include(s => s.UsuarioSolicitante)
                .Include(s => s.UsuarioResponde)
                .Where(s => s.UsuarioSolicitanteId == userId)
                .OrderByDescending(s => s.FechaSolicitud)
                .ToListAsync();

            return View(misSolicitudes);
        }

        // Slide 16 / CU-08: Revisar Solicitud
        [Authorize(Roles = Roles.AdministradorOSupervisor)]
        [HttpGet]
        public async Task<IActionResult> Revisar(int id)
        {
            var solicitud = await _context.Solicitudes
                .Include(s => s.UsuarioSolicitante)
                .Include(s => s.UsuarioResponde)
                .FirstOrDefaultAsync(s => s.Id == id);

            if (solicitud == null) return NotFound();

            if (!PuedeGestionar(solicitud))
            {
                TempData["ErrorMessage"] = MensajeSinPermisoSupervisor;
                return RedirectToAction(nameof(Index));
            }

            return View(solicitud);
        }

        // CU-08: el Supervisor solo revisa, aprueba o rechaza registros de empleados; el resto es del Administrador
        private const string TipoGestionableSupervisor = TiposSolicitud.RegistroEmpleado;
        private const string MensajeSinPermisoSupervisor = "Los Supervisores solo tienen permiso para gestionar solicitudes de registro de empleados.";

        private bool PuedeGestionar(Solicitud solicitud) =>
            !User.IsInRole(Roles.Supervisor) || solicitud.TipoSolicitud == TipoGestionableSupervisor;

        // Aprobar Solicitud (CU-08, CU-11, CU-14, CU-21, CU-41)
        [Authorize(Roles = Roles.AdministradorOSupervisor)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Aprobar(int id)
        {
            var solicitud = await _context.Solicitudes
                .Include(s => s.UsuarioSolicitante)
                .FirstOrDefaultAsync(s => s.Id == id);

            if (solicitud == null || solicitud.Estado != EstadosSolicitud.Pendiente)
            {
                TempData["ErrorMessage"] = "La solicitud no se encuentra disponible para aprobación.";
                return RedirectToAction(nameof(Index));
            }

            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            if (!PuedeGestionar(solicitud))
            {
                TempData["ErrorMessage"] = MensajeSinPermisoSupervisor;
                return RedirectToAction(nameof(Index));
            }

            try
            {
                await AplicarAsync(solicitud);

                solicitud.Estado = EstadosSolicitud.Aprobada;
                solicitud.FechaRespuesta = DateTime.Now;
                solicitud.UsuarioRespondeId = userId;

                // Notificar al usuario solicitante (CU-43)
                _context.Notificaciones.Add(new Notificacion
                {
                    UsuarioDestinoId = solicitud.UsuarioSolicitanteId,
                    Titulo = "Solicitud Aprobada",
                    Mensaje = $"Tu solicitud \"{TiposSolicitud.Nombre(solicitud.TipoSolicitud)}\" ha sido aprobada por el {User.DescripcionParaNotificacion()}.",
                    Tipo = "Success",
                    FechaCreacion = DateTime.Now,
                    Enlace = "/Solicitudes/MisSolicitudes"
                });

                await _context.SaveChangesAsync();

                await _auditService.RegistrarAsync("Aprobar Solicitud", "Solicitudes",
                    $"Solicitud #{solicitud.Id} ({solicitud.TipoSolicitud}) aprobada.");

                TempData["SuccessMessage"] = $"Solicitud #{solicitud.Id} aprobada correctamente.";
            }
            catch (ReglaNegocioException ex)
            {
                // La solicitud queda Pendiente: se puede aprobar cuando se resuelva el impedimento, o rechazar.
                TempData["ErrorMessage"] = ex.Message;
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Error al procesar la aprobación: {ex.Message}";
            }

            return RedirectToAction(nameof(Index));
        }

        // Rechazar Solicitud (CU-08, CU-11, CU-14, CU-21, CU-41)
        [Authorize(Roles = Roles.AdministradorOSupervisor)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Rechazar(int id, string motivoRechazo)
        {
            if (string.IsNullOrWhiteSpace(motivoRechazo))
            {
                TempData["ErrorMessage"] = "Debe indicar un motivo de rechazo obligatorio.";
                return RedirectToAction(nameof(Index));
            }

            var solicitud = await _context.Solicitudes.FindAsync(id);
            if (solicitud == null || solicitud.Estado != EstadosSolicitud.Pendiente)
            {
                TempData["ErrorMessage"] = "La solicitud no se encuentra disponible para rechazo.";
                return RedirectToAction(nameof(Index));
            }

            if (!PuedeGestionar(solicitud))
            {
                TempData["ErrorMessage"] = MensajeSinPermisoSupervisor;
                return RedirectToAction(nameof(Index));
            }

            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            solicitud.Estado = EstadosSolicitud.Rechazada;
            solicitud.MotivoRechazo = motivoRechazo.Trim();
            solicitud.FechaRespuesta = DateTime.Now;
            solicitud.UsuarioRespondeId = userId;

            // Notificar al usuario solicitante (CU-43)
            _context.Notificaciones.Add(new Notificacion
            {
                UsuarioDestinoId = solicitud.UsuarioSolicitanteId,
                Titulo = "Solicitud Rechazada",
                Mensaje = $"Tu solicitud \"{TiposSolicitud.Nombre(solicitud.TipoSolicitud)}\" fue rechazada. Motivo: {motivoRechazo}",
                Tipo = "Danger",
                FechaCreacion = DateTime.Now,
                Enlace = "/Solicitudes/MisSolicitudes"
            });

            await _context.SaveChangesAsync();

            await _auditService.RegistrarAsync("Rechazar Solicitud", "Solicitudes", 
                $"Solicitud #{solicitud.Id} rechazada. Motivo: {motivoRechazo}");

            TempData["SuccessMessage"] = $"Solicitud #{solicitud.Id} rechazada.";
            return RedirectToAction(nameof(Index));
        }

        // El empleado pudo tomar un pedido entre la creación de la solicitud y su aprobación.
        private async Task ValidarSinPedidoEnCursoAsync(Empleado empleado, string accion)
        {
            var pedidoEnCurso = await _context.Pedidos
                .Where(p => p.EmpleadoId == empleado.Id && PedidoEstados.EnCurso.Contains(p.Estado))
                .Select(p => p.CodigoPedido)
                .FirstOrDefaultAsync();

            if (pedidoEnCurso != null)
            {
                throw new ReglaNegocioException($"No se puede aprobar: el empleado '{empleado.NombreCompleto}' tiene el pedido {pedidoEnCurso} en curso y no es posible {accion}. Reasigne, complete o cancele el pedido primero; la solicitud sigue pendiente.");
            }
        }

        // Aplica los cambios de la solicitud. Las reglas se validan otra vez: entre la creación de la solicitud
        // y su aprobación otro registro pudo tomar la identificación, el correo o el código, o cambiar la jornada máxima.
        private Task AplicarAsync(Solicitud solicitud) => solicitud.TipoSolicitud switch
        {
            TiposSolicitud.RegistroEmpleado => RegistrarEmpleadoAsync(solicitud.LeerDatos<DatosEmpleadoSolicitud>()),
            TiposSolicitud.EdicionEmpleado => EditarEmpleadoAsync(solicitud.LeerDatos<SolicitudEdicionEmpleado>().NuevosDatos),
            TiposSolicitud.InactivacionEmpleado => InactivarEmpleadoAsync(solicitud.LeerDatos<SolicitudInactivacionEmpleado>()),
            TiposSolicitud.EdicionUbicacion => EditarUbicacionAsync(solicitud.LeerDatos<SolicitudEdicionUbicacion>()),
            TiposSolicitud.EliminacionUbicacion => EliminarUbicacionAsync(solicitud.LeerDatos<SolicitudEliminacionUbicacion>()),
            _ => throw new ReglaNegocioException($"No se puede aprobar: tipo de solicitud desconocido ({solicitud.TipoSolicitud}).")
        };

        private async Task RegistrarEmpleadoAsync(DatosEmpleadoSolicitud datos)
        {
            var identificacion = datos.Identificacion.Trim();
            var correo = ReglasEmpleado.NormalizarCorreo(datos.Correo);
            await ValidarReglasEmpleadoAsync(identificacion, correo, datos.HorarioEntrada, datos.HorarioSalida, empleadoId: null);

            _context.Empleados.Add(new Empleado
            {
                Identificacion = identificacion,
                NombreCompleto = datos.NombreCompleto,
                Correo = correo,
                HorarioEntrada = datos.HorarioEntrada,
                HorarioSalida = datos.HorarioSalida,
                JornadaLaboral = ReglasEmpleado.DescribirJornada(datos.HorarioEntrada, datos.HorarioSalida),
                FechaIngreso = datos.FechaIngreso
            });
        }

        private async Task EditarEmpleadoAsync(DatosEmpleadoSolicitud datos)
        {
            var empleado = await _context.Empleados.FindAsync(datos.Id)
                ?? throw new ReglaNegocioException("No se puede aprobar: el empleado ya no existe. Rechace la solicitud.");

            var correo = ReglasEmpleado.NormalizarCorreo(datos.Correo);
            await ValidarReglasEmpleadoAsync(empleado.Identificacion, correo, datos.HorarioEntrada, datos.HorarioSalida, empleado.Id);

            empleado.NombreCompleto = datos.NombreCompleto;
            empleado.Correo = correo;
            empleado.HorarioEntrada = datos.HorarioEntrada;
            empleado.HorarioSalida = datos.HorarioSalida;
            empleado.JornadaLaboral = ReglasEmpleado.DescribirJornada(datos.HorarioEntrada, datos.HorarioSalida);
            empleado.FechaIngreso = datos.FechaIngreso;
        }

        private async Task InactivarEmpleadoAsync(SolicitudInactivacionEmpleado datos)
        {
            var empleado = await _context.Empleados.FindAsync(datos.EmpleadoId)
                ?? throw new ReglaNegocioException("No se puede aprobar: el empleado ya no existe. Rechace la solicitud.");

            await ValidarSinPedidoEnCursoAsync(empleado, "inactivarlo");
            empleado.Activo = false;
        }

        private async Task EditarUbicacionAsync(SolicitudEdicionUbicacion datos)
        {
            var ubicacion = await _context.Ubicaciones
                .Include(u => u.Producto)
                .FirstOrDefaultAsync(u => u.Id == datos.UbicacionId)
                ?? throw new ReglaNegocioException("No se puede aprobar: la ubicación ya no existe. Rechace la solicitud.");

            var nuevos = datos.NuevosDatos;
            var duplicada = await _context.Ubicaciones.AnyAsync(u =>
                u.BodegaId == ubicacion.BodegaId && u.CodigoUbicacion == nuevos.CodigoUbicacion && u.Id != ubicacion.Id);
            if (duplicada)
            {
                throw new ReglaNegocioException($"No se puede aprobar: ya existe otra ubicación con el código '{nuevos.CodigoUbicacion}' en esta bodega. La solicitud sigue pendiente.");
            }

            var nombreProducto = nuevos.NombreProducto ?? nuevos.CodigoProducto;
            var producto = await _context.Productos
                .FirstOrDefaultAsync(p => p.Codigo.ToUpper() == nuevos.CodigoProducto.ToUpper());
            if (producto == null)
            {
                producto = new Producto { Codigo = nuevos.CodigoProducto, Nombre = nombreProducto };
                _context.Productos.Add(producto);
            }
            else
            {
                producto.Nombre = nombreProducto;
            }

            // Si la categoría fue eliminada mientras tanto, el producto queda sin clasificar
            var categoriaId = nuevos.CategoriaProductoId;
            if (categoriaId.HasValue && !await _context.CategoriasProducto.AnyAsync(c => c.Id == categoriaId.Value))
            {
                categoriaId = null;
            }
            producto.CategoriaProductoId = categoriaId;
            if (nuevos.UnidadesPorCaja.HasValue)
            {
                producto.UnidadesPorCaja = Math.Max(1, nuevos.UnidadesPorCaja.Value);
            }

            ubicacion.Pasillo = nuevos.Pasillo;
            ubicacion.Estante = nuevos.Estante;
            ubicacion.Nivel = nuevos.Nivel;
            ubicacion.CodigoUbicacion = nuevos.CodigoUbicacion;
            ubicacion.Activa = nuevos.Activa;
            ubicacion.Producto = producto;
        }

        private async Task EliminarUbicacionAsync(SolicitudEliminacionUbicacion datos)
        {
            var ubicacion = await _context.Ubicaciones.FindAsync(datos.UbicacionId)
                ?? throw new ReglaNegocioException("No se puede aprobar: la ubicación ya no existe. Rechace la solicitud.");

            // Pudo recibir pedidos después de creada la solicitud
            if (await _context.DetallesPedido.AnyAsync(d => d.UbicacionId == ubicacion.Id))
            {
                throw new ReglaNegocioException($"No se puede aprobar: la ubicación '{ubicacion.CodigoUbicacion}' tiene pedidos asociados. Rechace la solicitud.");
            }
            _context.Ubicaciones.Remove(ubicacion);
        }

        private async Task ValidarReglasEmpleadoAsync(string identificacion, string? correo, TimeSpan entrada, TimeSpan salida, int? empleadoId)
        {
            var error = await ReglasEmpleado.ValidarAsync(_context, identificacion, correo, entrada, salida, empleadoId);
            if (error != null)
            {
                throw new ReglaNegocioException($"No se puede aprobar: {error.Mensaje} La solicitud sigue pendiente.");
            }
        }

        private sealed class ReglaNegocioException : Exception
        {
            public ReglaNegocioException(string message) : base(message) { }
        }
    }
}
