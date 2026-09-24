using System.Security.Claims;
using System.Text.Json;
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
        [Authorize(Roles = "Administrador,Supervisor")]
        public async Task<IActionResult> Index(string? estado = null)
        {
            var userRole = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role)?.Value;

            var query = _context.Solicitudes
                .Include(s => s.UsuarioSolicitante)
                .Include(s => s.UsuarioResponde)
                .AsQueryable();

            // Supervisor solo puede aprobar/rechazar solicitudes de Registro de Empleado (CU-08)
            if (userRole == "Supervisor")
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
                query = query.OrderByDescending(s => s.Estado == "Pendiente")
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
        [Authorize(Roles = "Administrador,Supervisor")]
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
        private const string TipoGestionableSupervisor = "RegistroEmpleado";
        private const string MensajeSinPermisoSupervisor = "Los Supervisores solo tienen permiso para gestionar solicitudes de registro de empleados.";

        private bool PuedeGestionar(Solicitud solicitud) =>
            !User.IsInRole("Supervisor") || solicitud.TipoSolicitud == TipoGestionableSupervisor;

        // Aprobar Solicitud (CU-08, CU-11, CU-14, CU-21, CU-41)
        [Authorize(Roles = "Administrador,Supervisor")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Aprobar(int id)
        {
            var solicitud = await _context.Solicitudes
                .Include(s => s.UsuarioSolicitante)
                .FirstOrDefaultAsync(s => s.Id == id);

            if (solicitud == null || solicitud.Estado != "Pendiente")
            {
                TempData["ErrorMessage"] = "La solicitud no se encuentra disponible para aprobación.";
                return RedirectToAction(nameof(Index));
            }

            var userEmail = User.FindFirstValue(ClaimTypes.Email) ?? "Usuario";
            var userRole = User.FindFirstValue(ClaimTypes.Role) ?? "Usuario";
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            if (!PuedeGestionar(solicitud))
            {
                TempData["ErrorMessage"] = MensajeSinPermisoSupervisor;
                return RedirectToAction(nameof(Index));
            }

            try
            {
                // Ejecutar la acción según el tipo de solicitud
                switch (solicitud.TipoSolicitud)
                {
                    case "RegistroEmpleado":
                        var empleado = JsonSerializer.Deserialize<Empleado>(solicitud.DatosJson);
                        if (empleado != null)
                        {
                            empleado.Id = 0; // Para nuevo registro
                            _context.Empleados.Add(empleado);
                        }
                        break;

                    case "EdicionEmpleado":
                        using (var doc = JsonDocument.Parse(solicitud.DatosJson))
                        {
                            if (doc.RootElement.TryGetProperty("NuevosDatos", out var nuevosDatosEl))
                            {
                                var empData = JsonSerializer.Deserialize<Empleado>(nuevosDatosEl.GetRawText());
                                if (empData != null)
                                {
                                    var empExistente = await _context.Empleados.FindAsync(empData.Id);
                                    if (empExistente != null)
                                    {
                                        if (empData.EstadoDisponibilidad != empExistente.EstadoDisponibilidad)
                                        {
                                            await ValidarSinPedidoEnCursoAsync(empExistente, "cambiar su estado");
                                        }

                                        empExistente.NombreCompleto = empData.NombreCompleto;
                                        empExistente.Correo = empData.Correo;
                                        empExistente.JornadaLaboral = empData.JornadaLaboral;
                                        empExistente.HorarioEntrada = empData.HorarioEntrada;
                                        empExistente.HorarioSalida = empData.HorarioSalida;
                                        empExistente.FechaIngreso = empData.FechaIngreso;
                                        empExistente.EstadoDisponibilidad = empData.EstadoDisponibilidad;
                                    }
                                }
                            }
                        }
                        break;

                    case "InactivacionEmpleado":
                        using (var doc = JsonDocument.Parse(solicitud.DatosJson))
                        {
                            if (doc.RootElement.TryGetProperty("EmpleadoId", out var empIdProp))
                            {
                                var emp = await _context.Empleados.FindAsync(empIdProp.GetInt32());
                                if (emp != null)
                                {
                                    await ValidarSinPedidoEnCursoAsync(emp, "inactivarlo");
                                    emp.Activo = false;
                                    emp.EstadoDisponibilidad = "Inactivo";
                                }
                            }
                        }
                        break;

                    case "EdicionUbicacion":
                        using (var doc = JsonDocument.Parse(solicitud.DatosJson))
                        {
                            var root = doc.RootElement;
                            if (root.TryGetProperty("UbicacionId", out var ubiEditIdProp))
                            {
                                var ubiEdit = await _context.Ubicaciones
                                    .Include(u => u.Producto)
                                    .FirstOrDefaultAsync(u => u.Id == ubiEditIdProp.GetInt32());

                                if (ubiEdit != null && root.TryGetProperty("NuevosDatos", out var nuevosDatosUbiEl))
                                {
                                    var codigoUbicacionNuevo = nuevosDatosUbiEl.GetProperty("CodigoUbicacion").GetString()!;

                                    var duplicada = await _context.Ubicaciones.AnyAsync(u =>
                                        u.BodegaId == ubiEdit.BodegaId && u.CodigoUbicacion == codigoUbicacionNuevo && u.Id != ubiEdit.Id);
                                    if (duplicada)
                                    {
                                        throw new InvalidOperationException($"Ya existe otra ubicación con el código '{codigoUbicacionNuevo}' en esta bodega. La solicitud no se pudo aplicar.");
                                    }

                                    var codigoProductoNuevo = nuevosDatosUbiEl.GetProperty("CodigoProducto").GetString()!;
                                    var nombreProductoNuevo = nuevosDatosUbiEl.GetProperty("NombreProducto").GetString() ?? codigoProductoNuevo;

                                    var productoUbi = await _context.Productos
                                        .FirstOrDefaultAsync(p => p.Codigo.ToUpper() == codigoProductoNuevo.ToUpper());
                                    if (productoUbi == null)
                                    {
                                        productoUbi = new Producto { Codigo = codigoProductoNuevo, Nombre = nombreProductoNuevo };
                                        _context.Productos.Add(productoUbi);
                                    }
                                    else
                                    {
                                        productoUbi.Nombre = nombreProductoNuevo;
                                    }

                                    ubiEdit.Pasillo = nuevosDatosUbiEl.GetProperty("Pasillo").GetString() ?? ubiEdit.Pasillo;
                                    ubiEdit.Estante = nuevosDatosUbiEl.GetProperty("Estante").GetString() ?? ubiEdit.Estante;
                                    ubiEdit.Nivel = nuevosDatosUbiEl.GetProperty("Nivel").GetString() ?? ubiEdit.Nivel;
                                    ubiEdit.CodigoUbicacion = codigoUbicacionNuevo;
                                    ubiEdit.Activa = nuevosDatosUbiEl.TryGetProperty("Activa", out var activaProp) && activaProp.GetBoolean();
                                    ubiEdit.Producto = productoUbi;

                                    // Solicitudes nuevas traen la categoría; si fue eliminada mientras tanto, queda sin clasificar
                                    if (nuevosDatosUbiEl.TryGetProperty("CategoriaProductoId", out var categoriaProp))
                                    {
                                        int? categoriaId = categoriaProp.ValueKind == JsonValueKind.Number ? categoriaProp.GetInt32() : null;
                                        if (categoriaId.HasValue && !await _context.CategoriasProducto.AnyAsync(c => c.Id == categoriaId.Value))
                                        {
                                            categoriaId = null;
                                        }
                                        productoUbi.CategoriaProductoId = categoriaId;
                                    }
                                }
                            }
                        }
                        break;

                    case "EliminacionUbicacion":
                        using (var doc = JsonDocument.Parse(solicitud.DatosJson))
                        {
                            if (doc.RootElement.TryGetProperty("UbicacionId", out var ubiIdProp))
                            {
                                var ubi = await _context.Ubicaciones.FindAsync(ubiIdProp.GetInt32());
                                if (ubi != null)
                                {
                                    _context.Ubicaciones.Remove(ubi);
                                }
                            }
                        }
                        break;

                }

                solicitud.Estado = "Aprobada";
                solicitud.FechaRespuesta = DateTime.Now;
                solicitud.UsuarioRespondeId = userId;

                // Notificar al usuario solicitante (CU-43)
                _context.Notificaciones.Add(new Notificacion
                {
                    UsuarioDestinoId = solicitud.UsuarioSolicitanteId,
                    Titulo = "Solicitud Aprobada",
                    Mensaje = $"Tu solicitud de {solicitud.TipoSolicitud} ha sido aprobada por el {User.DescripcionParaNotificacion()}.",
                    Tipo = "Success",
                    FechaCreacion = DateTime.Now,
                    Enlace = "/Solicitudes/MisSolicitudes"
                });

                await _context.SaveChangesAsync();

                await _auditService.LogAsync(userEmail, userRole, "Aprobar Solicitud", "Solicitudes",
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
        [Authorize(Roles = "Administrador,Supervisor")]
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
            if (solicitud == null || solicitud.Estado != "Pendiente")
            {
                TempData["ErrorMessage"] = "La solicitud no se encuentra disponible para rechazo.";
                return RedirectToAction(nameof(Index));
            }

            if (!PuedeGestionar(solicitud))
            {
                TempData["ErrorMessage"] = MensajeSinPermisoSupervisor;
                return RedirectToAction(nameof(Index));
            }

            var userEmail = User.FindFirstValue(ClaimTypes.Email) ?? "Usuario";
            var userRole = User.FindFirstValue(ClaimTypes.Role) ?? "Usuario";
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            solicitud.Estado = "Rechazada";
            solicitud.MotivoRechazo = motivoRechazo.Trim();
            solicitud.FechaRespuesta = DateTime.Now;
            solicitud.UsuarioRespondeId = userId;

            // Notificar al usuario solicitante (CU-43)
            _context.Notificaciones.Add(new Notificacion
            {
                UsuarioDestinoId = solicitud.UsuarioSolicitanteId,
                Titulo = "Solicitud Rechazada",
                Mensaje = $"Tu solicitud de {solicitud.TipoSolicitud} fue rechazada. Motivo: {motivoRechazo}",
                Tipo = "Danger",
                FechaCreacion = DateTime.Now,
                Enlace = "/Solicitudes/MisSolicitudes"
            });

            await _context.SaveChangesAsync();

            await _auditService.LogAsync(userEmail, userRole, "Rechazar Solicitud", "Solicitudes", 
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

        private sealed class ReglaNegocioException : Exception
        {
            public ReglaNegocioException(string message) : base(message) { }
        }
    }
}
