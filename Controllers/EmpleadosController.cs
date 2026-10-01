using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickingApp.Data;
using PickingApp.Models;
using PickingApp.Services;

namespace PickingApp.Controllers
{
    // Gestión de empleados de bodega. Según el rol, un cambio se aplica directamente
    // o se convierte en una Solicitud que otro rol debe aprobar.
    [Authorize]
    public class EmpleadosController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IAuditService _auditService;
        private readonly IPickingEngine _pickingEngine;

        public EmpleadosController(ApplicationDbContext context, IAuditService auditService, IPickingEngine pickingEngine)
        {
            _context = context;
            _auditService = auditService;
            _pickingEngine = pickingEngine;
        }

        // CU-16: Consultar empleados
        public async Task<IActionResult> Index()
        {
            var empleados = await _context.Empleados
                .OrderBy(e => e.NombreCompleto)
                .ToListAsync();

            // Un empleado está "Ocupado" si tiene un pedido Asignado o Reasignado
            ViewBag.EmpleadosOcupados = (await _context.Pedidos
                .Where(p => p.EmpleadoId != null && PedidoEstados.EnCurso.Contains(p.Estado))
                .Select(p => p.EmpleadoId!.Value)
                .ToListAsync())
                .ToHashSet();
            // Factor de rendimiento de cada empleado (PickingEngine.CalcularRendimientosAsync)
            ViewBag.Rendimientos = await _pickingEngine.CalcularRendimientosAsync(empleados.Select(e => e.Id).ToList());
            return View(empleados);
        }

        // CU-06 y CU-07: Registrar Empleado
        [HttpGet]
        public IActionResult Crear()
        {
            var model = new Empleado
            {
                FechaIngreso = DateTime.Today,
                HorarioEntrada = new TimeSpan(8, 0, 0),
                HorarioSalida = new TimeSpan(17, 0, 0)
            };
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Crear([Bind(nameof(Empleado.Identificacion), nameof(Empleado.NombreCompleto), nameof(Empleado.Correo),
            nameof(Empleado.HorarioEntrada), nameof(Empleado.HorarioSalida), nameof(Empleado.FechaIngreso))] Empleado model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            // Reglas que necesitan consultar la BD: identificación y correo únicos, duración máxima de jornada
            model.Identificacion = model.Identificacion.Trim();
            model.Correo = ReglasEmpleado.NormalizarCorreo(model.Correo);
            var error = await ReglasEmpleado.ValidarAsync(_context, model.Identificacion, model.Correo,
                model.HorarioEntrada, model.HorarioSalida, empleadoId: null);
            if (error != null)
            {
                ModelState.AddModelError(error.Campo, error.Mensaje);
                return View(model);
            }

            // Rol e Id del usuario que tiene la sesión, leídos de la cookie
            var userRole = User.FindFirstValue(ClaimTypes.Role) ?? Roles.Auxiliar;
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            // Administrador y Supervisor: Creación directa (CU-06)
            if (userRole == Roles.Administrador || userRole == Roles.Supervisor)
            {
                _context.Empleados.Add(model);
                await _context.SaveChangesAsync();
                TempData["EmpleadoDestacadoId"] = model.Id;

                await _auditService.RegistrarAsync("Registrar Empleado", "Empleados", 
                    $"Empleado registrado: {model.Identificacion} - {model.NombreCompleto}");

                TempData["SuccessMessage"] = $"Empleado '{model.NombreCompleto}' registrado exitosamente.";
            }
            // Auxiliar: Solicitar registro de empleado (CU-07)
            else
            {
                var pendiente = await SolicitudesPendientes.DeRegistroAsync(_context, model.Identificacion);
                if (pendiente != null)
                {
                    ModelState.AddModelError(nameof(Empleado.Identificacion), SolicitudesPendientes.Mensaje(pendiente, "un empleado con esta identificación"));
                    return View(model);
                }

                // El empleado aún no se crea: sus datos se guardan como JSON en la solicitud
                // y se insertan en Empleados solo cuando la solicitud se aprueba
                var solicitud = Solicitud.Nueva(TiposSolicitud.RegistroEmpleado, userId, DatosEmpleadoSolicitud.Desde(model));

                _context.Solicitudes.Add(solicitud);
                _context.Notificaciones.Add(new Notificacion
                {
                    RolDestino = Roles.Supervisor,
                    Titulo = "Solicitud de Nuevo Empleado",
                    Mensaje = $"El {User.DescripcionParaNotificacion()} solicitó el registro del empleado {model.NombreCompleto}.",
                    Tipo = "Info",
                    FechaCreacion = DateTime.Now,
                    Enlace = "/Solicitudes/Index"
                });

                await _context.SaveChangesAsync();

                await _auditService.RegistrarAsync("Solicitud Registro Empleado", "Solicitudes", 
                    $"Auxiliar solicitó registro de empleado: {model.NombreCompleto}");

                TempData["SuccessMessage"] = $"Se ha generado la solicitud para registrar a '{model.NombreCompleto}'. Requiere aprobación de un Supervisor o Administrador.";
            }

            return RedirectToAction(nameof(Index));
        }

        // CU-09 y CU-10: Editar datos de empleado
        [HttpGet]
        public async Task<IActionResult> Editar(int id)
        {
            var empleado = await _context.Empleados.FindAsync(id);
            if (empleado == null) return NotFound();

            return View(empleado);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Editar([Bind(nameof(Empleado.Id), nameof(Empleado.Identificacion), nameof(Empleado.NombreCompleto),
            nameof(Empleado.Correo), nameof(Empleado.HorarioEntrada), nameof(Empleado.HorarioSalida), nameof(Empleado.FechaIngreso))] Empleado model,
            string? motivo)
        {
            motivo = (motivo ?? string.Empty).Trim();

            if (!ModelState.IsValid)
            {
                ViewBag.Motivo = motivo;
                return View(model);
            }

            var empleadoExistente = await _context.Empleados.FindAsync(model.Id);
            if (empleadoExistente == null) return NotFound();

            model.Correo = ReglasEmpleado.NormalizarCorreo(model.Correo);
            var error = await ReglasEmpleado.ValidarAsync(_context, empleadoExistente.Identificacion, model.Correo,
                model.HorarioEntrada, model.HorarioSalida, empleadoExistente.Id);
            if (error != null)
            {
                ModelState.AddModelError(error.Campo, error.Mensaje);
                ViewBag.Motivo = motivo;
                return View(model);
            }

            var userRole = User.FindFirstValue(ClaimTypes.Role) ?? Roles.Auxiliar;
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            // Administrador: Edición directa (CU-09)
            if (userRole == Roles.Administrador)
            {
                empleadoExistente.NombreCompleto = model.NombreCompleto;
                empleadoExistente.Correo = model.Correo;
                empleadoExistente.HorarioEntrada = model.HorarioEntrada;
                empleadoExistente.HorarioSalida = model.HorarioSalida;
                empleadoExistente.FechaIngreso = model.FechaIngreso;

                await _context.SaveChangesAsync();
                TempData["EmpleadoDestacadoId"] = empleadoExistente.Id;

                await _auditService.RegistrarAsync("Editar Empleado", "Empleados", 
                    $"Datos actualizados de empleado: {empleadoExistente.Identificacion}");

                TempData["SuccessMessage"] = $"Datos de '{empleadoExistente.NombreCompleto}' actualizados exitosamente.";
            }
            // Supervisor y Auxiliar: Solicitar edición de empleado (CU-10)
            else
            {
                if (string.IsNullOrWhiteSpace(motivo))
                {
                    ModelState.AddModelError(string.Empty, "Debe indicar el motivo de la edición.");
                    ViewBag.Motivo = motivo;
                    return View(model);
                }

                // Lista de campos que cambiaron (valor anterior → nuevo) para que el Administrador los revise
                var cambios = new List<CambioSolicitud>();
                void AgregarCambio(string campo, string anterior, string nuevo)
                {
                    if (!string.Equals(anterior, nuevo, StringComparison.Ordinal))
                    {
                        cambios.Add(new CambioSolicitud { Campo = campo, Anterior = anterior, Nuevo = nuevo });
                    }
                }

                AgregarCambio(CamposEmpleado.Nombre, empleadoExistente.NombreCompleto, model.NombreCompleto);
                AgregarCambio(CamposEmpleado.Correo, empleadoExistente.Correo ?? "-", model.Correo ?? "-");
                AgregarCambio(CamposEmpleado.FechaIngreso, empleadoExistente.FechaIngreso.ToString("dd/MM/yyyy"), model.FechaIngreso.ToString("dd/MM/yyyy"));
                AgregarCambio(CamposEmpleado.HoraEntrada, FormatHora(empleadoExistente.HorarioEntrada), FormatHora(model.HorarioEntrada));
                AgregarCambio(CamposEmpleado.HoraSalida, FormatHora(empleadoExistente.HorarioSalida), FormatHora(model.HorarioSalida));

                // Una solicitud sin cambios no tiene nada que aprobar; una segunda sobre el mismo empleado
                // mostraría valores "anteriores" que ya no serían los reales al aprobarla
                var pendiente = await SolicitudesPendientes.DeEmpleadoAsync(_context, empleadoExistente.Id);
                var errorSolicitud = cambios.Count == 0 ? "No cambió ningún dato del empleado: no hay nada que solicitar."
                    : pendiente != null ? SolicitudesPendientes.Mensaje(pendiente, "este empleado")
                    : null;
                if (errorSolicitud != null)
                {
                    ModelState.AddModelError(string.Empty, errorSolicitud);
                    ViewBag.Motivo = motivo;
                    return View(model);
                }

                var solicitud = Solicitud.Nueva(TiposSolicitud.EdicionEmpleado, userId, new SolicitudEdicionEmpleado
                {
                    Motivo = motivo,
                    Cambios = cambios,
                    NuevosDatos = DatosEmpleadoSolicitud.Desde(model)
                });

                _context.Solicitudes.Add(solicitud);
                _context.Notificaciones.Add(new Notificacion
                {
                    RolDestino = Roles.Administrador,
                    Titulo = "Solicitud de Edición de Empleado",
                    Mensaje = $"El {User.DescripcionParaNotificacion()} solicitó modificar los datos del empleado {model.NombreCompleto}.",
                    Tipo = "Info",
                    FechaCreacion = DateTime.Now,
                    Enlace = "/Solicitudes/Index"
                });

                await _context.SaveChangesAsync();

                await _auditService.RegistrarAsync("Solicitud Edición Empleado", "Solicitudes",
                    $"Solicitud de edición enviada para empleado {model.NombreCompleto}. Motivo: {motivo}");

                TempData["SuccessMessage"] = $"Se ha generado la solicitud de modificación para '{model.NombreCompleto}'. Requiere aprobación del Administrador.";
            }

            return RedirectToAction(nameof(Index));
        }

        // CU-12, CU-13: Inactivar empleado
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Inactivar(int id, string? motivo)
        {
            var empleado = await _context.Empleados.FindAsync(id);
            if (empleado == null) return NotFound();

            // No se puede inactivar (ni solicitar inactivar) a un empleado con un pedido en curso
            if (empleado.Activo)
            {
                var pedidoEnCurso = await ObtenerPedidoEnCursoAsync(empleado.Id);
                if (pedidoEnCurso != null)
                {
                    TempData["ErrorMessage"] = $"No se puede inactivar a '{empleado.NombreCompleto}' porque tiene el pedido {pedidoEnCurso} en curso. Reasigne, complete o cancele el pedido primero.";
                    return RedirectToAction(nameof(Index));
                }
            }

            var userRole = User.FindFirstValue(ClaimTypes.Role) ?? Roles.Auxiliar;
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            // Administrador: Inactivación Directa (CU-12)
            if (userRole == Roles.Administrador)
            {
                // El mismo botón inactiva o reactiva: invierte el estado actual
                empleado.Activo = !empleado.Activo;
                await _context.SaveChangesAsync();
                TempData["EmpleadoDestacadoId"] = empleado.Id;

                var accion = empleado.Activo ? "Reactivar Empleado" : "Inactivar Empleado";
                await _auditService.RegistrarAsync(accion, "Empleados", 
                    $"Estado de empleado {empleado.Identificacion} cambiado a {(empleado.Activo ? "Activo" : "Inactivo")}");

                TempData["SuccessMessage"] = $"El empleado '{empleado.NombreCompleto}' ahora está {(empleado.Activo ? "Activo" : "Inactivo")}.";
            }
            // Supervisor y Auxiliar: Solicitar Inactivación (CU-13)
            else
            {
                motivo = (motivo ?? string.Empty).Trim();
                var pendiente = await SolicitudesPendientes.DeEmpleadoAsync(_context, empleado.Id);
                var errorSolicitud = !empleado.Activo ? $"'{empleado.NombreCompleto}' ya está inactivo."
                    : motivo.Length == 0 ? "Debe indicar el motivo de la inactivación."
                    : pendiente != null ? SolicitudesPendientes.Mensaje(pendiente, $"'{empleado.NombreCompleto}'")
                    : null;
                if (errorSolicitud != null)
                {
                    TempData["ErrorMessage"] = errorSolicitud;
                    return RedirectToAction(nameof(Index));
                }

                var solicitud = Solicitud.Nueva(TiposSolicitud.InactivacionEmpleado, userId, new SolicitudInactivacionEmpleado
                {
                    EmpleadoId = empleado.Id,
                    Identificacion = empleado.Identificacion,
                    NombreCompleto = empleado.NombreCompleto,
                    Motivo = motivo
                });

                _context.Solicitudes.Add(solicitud);
                _context.Notificaciones.Add(new Notificacion
                {
                    RolDestino = Roles.Administrador,
                    Titulo = "Solicitud de Inactivación de Empleado",
                    Mensaje = $"El {User.DescripcionParaNotificacion()} solicitó inactivar al empleado {empleado.NombreCompleto}.",
                    Tipo = "Warning",
                    FechaCreacion = DateTime.Now,
                    Enlace = "/Solicitudes/Index"
                });

                await _context.SaveChangesAsync();

                await _auditService.RegistrarAsync("Solicitud Inactivar Empleado", "Solicitudes", 
                    $"Solicitud de inactivación para {empleado.NombreCompleto}. Motivo: {motivo}");

                TempData["SuccessMessage"] = $"Se ha generado la solicitud de inactivación para '{empleado.NombreCompleto}'. Requiere aprobación del Administrador.";
            }

            return RedirectToAction(nameof(Index));
        }

        // Reiniciar el historial de tiempos usado para calcular el rendimiento del empleado (Solo Administrador).
        // No elimina ni desvincula los pedidos completados: solo deja de contarlos a partir de ahora.
        [Authorize(Roles = Roles.Administrador)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ReiniciarPromedio(int id)
        {
            var empleado = await _context.Empleados.FindAsync(id);
            if (empleado == null) return NotFound();

            empleado.FechaReinicioPromedio = DateTime.Now;
            await _context.SaveChangesAsync();
            TempData["EmpleadoDestacadoId"] = empleado.Id;

            await _auditService.RegistrarAsync("Reiniciar Rendimiento", "Empleados",
                $"Se reinició el historial de tiempos del empleado {empleado.Identificacion} - {empleado.NombreCompleto}. Los pedidos completados antes de esta fecha ya no se contarán para su rendimiento.");

            TempData["SuccessMessage"] = $"El historial de tiempos de '{empleado.NombreCompleto}' fue reiniciado. Sus pedidos anteriores se conservan, pero no se contarán para su rendimiento.";
            return RedirectToAction(nameof(Index));
        }

        private async Task<string?> ObtenerPedidoEnCursoAsync(int empleadoId)
        {
            return await _context.Pedidos
                .Where(p => p.EmpleadoId == empleadoId && PedidoEstados.EnCurso.Contains(p.Estado))
                .Select(p => p.CodigoPedido)
                .FirstOrDefaultAsync();
        }

        private static string FormatHora(TimeSpan t) => t.ToString(@"hh\:mm");
    }
}
