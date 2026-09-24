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
                .Include(e => e.PedidosAsignados)
                .OrderBy(e => e.NombreCompleto)
                .ToListAsync();

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
        public async Task<IActionResult> Crear(Empleado model)
        {
            // Todo empleado nuevo se registra siempre como Disponible (no aplica para CU-06/07).
            model.EstadoDisponibilidad = "Disponible";
            model.JornadaLaboral = $"{FormatHora(model.HorarioEntrada)} - {FormatHora(model.HorarioSalida)}";

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var maxHoras = await ObtenerMaxHorasJornadaAsync();
            var duracionHoras = CalcularDuracionJornadaHoras(model.HorarioEntrada, model.HorarioSalida);
            if (duracionHoras > maxHoras)
            {
                ModelState.AddModelError("HorarioSalida", $"La jornada laboral no puede superar las {maxHoras:0.#} horas (duración indicada: {duracionHoras:0.#} horas).");
                return View(model);
            }

            model.Identificacion = model.Identificacion.Trim();
            if (await _context.Empleados.AnyAsync(e => e.Identificacion == model.Identificacion))
            {
                ModelState.AddModelError("Identificacion", "Ya existe un empleado registrado con esta identificación.");
                return View(model);
            }

            model.Correo = string.IsNullOrWhiteSpace(model.Correo) ? null : model.Correo.Trim().ToLower();
            if (model.Correo != null && !await CorreoDisponibleAsync(model.Correo))
            {
                ModelState.AddModelError("Correo", "Ese correo ya está en uso por otro empleado o por un usuario del sistema.");
                return View(model);
            }

            var userEmail = User.FindFirstValue(ClaimTypes.Email) ?? "Usuario";
            var userRole = User.FindFirstValue(ClaimTypes.Role) ?? "Auxiliar";
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            // Administrador y Supervisor: Creación directa (CU-06)
            if (userRole == "Administrador" || userRole == "Supervisor")
            {
                _context.Empleados.Add(model);
                await _context.SaveChangesAsync();
                TempData["EmpleadoDestacadoId"] = model.Id;

                await _auditService.LogAsync(userEmail, userRole, "Registrar Empleado", "Empleados", 
                    $"Empleado registrado: {model.Identificacion} - {model.NombreCompleto}");

                TempData["SuccessMessage"] = $"Empleado '{model.NombreCompleto}' registrado exitosamente.";
            }
            // Auxiliar: Solicitar registro de empleado (CU-07)
            else
            {
                var solicitud = new Solicitud
                {
                    TipoSolicitud = "RegistroEmpleado",
                    UsuarioSolicitanteId = userId,
                    Estado = "Pendiente",
                    FechaSolicitud = DateTime.Now,
                    DatosJson = System.Text.Json.JsonSerializer.Serialize(model)
                };

                _context.Solicitudes.Add(solicitud);
                _context.Notificaciones.Add(new Notificacion
                {
                    RolDestino = "Supervisor",
                    Titulo = "Solicitud de Nuevo Empleado",
                    Mensaje = $"El {User.DescripcionParaNotificacion()} solicitó el registro del empleado {model.NombreCompleto}.",
                    Tipo = "Info",
                    FechaCreacion = DateTime.Now,
                    Enlace = "/Solicitudes/Index"
                });

                await _context.SaveChangesAsync();

                await _auditService.LogAsync(userEmail, userRole, "Solicitud Registro Empleado", "Solicitudes", 
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
        public async Task<IActionResult> Editar(Empleado model, string? motivo)
        {
            model.JornadaLaboral = $"{FormatHora(model.HorarioEntrada)} - {FormatHora(model.HorarioSalida)}";
            motivo = (motivo ?? string.Empty).Trim();

            if (!ModelState.IsValid)
            {
                ViewBag.Motivo = motivo;
                return View(model);
            }

            var maxHoras = await ObtenerMaxHorasJornadaAsync();
            var duracionHoras = CalcularDuracionJornadaHoras(model.HorarioEntrada, model.HorarioSalida);
            if (duracionHoras > maxHoras)
            {
                ModelState.AddModelError("HorarioSalida", $"La jornada laboral no puede superar las {maxHoras:0.#} horas (duración indicada: {duracionHoras:0.#} horas).");
                return View(model);
            }

            var empleadoExistente = await _context.Empleados.FindAsync(model.Id);
            if (empleadoExistente == null) return NotFound();

            model.Correo = string.IsNullOrWhiteSpace(model.Correo) ? null : model.Correo.Trim().ToLower();
            if (model.Correo != null && !await CorreoDisponibleAsync(model.Correo, model.Id))
            {
                ModelState.AddModelError("Correo", "Ese correo ya está en uso por otro empleado o por un usuario del sistema.");
                ViewBag.Motivo = motivo;
                return View(model);
            }

            // Con un pedido en curso el estado lo controla el flujo del pedido, no la edición manual
            if (model.EstadoDisponibilidad != empleadoExistente.EstadoDisponibilidad)
            {
                var pedidoEnCurso = await ObtenerPedidoEnCursoAsync(empleadoExistente.Id);
                if (pedidoEnCurso != null)
                {
                    ModelState.AddModelError("EstadoDisponibilidad", $"No se puede cambiar el estado porque el empleado tiene el pedido {pedidoEnCurso} en curso. Reasigne, complete o cancele el pedido primero.");
                    ViewBag.Motivo = motivo;
                    return View(model);
                }
            }

            var userEmail = User.FindFirstValue(ClaimTypes.Email) ?? "Usuario";
            var userRole = User.FindFirstValue(ClaimTypes.Role) ?? "Auxiliar";
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            // Administrador: Edición directa (CU-09)
            if (userRole == "Administrador")
            {
                empleadoExistente.NombreCompleto = model.NombreCompleto;
                empleadoExistente.Correo = model.Correo;
                empleadoExistente.JornadaLaboral = model.JornadaLaboral;
                empleadoExistente.HorarioEntrada = model.HorarioEntrada;
                empleadoExistente.HorarioSalida = model.HorarioSalida;
                empleadoExistente.FechaIngreso = model.FechaIngreso;
                empleadoExistente.EstadoDisponibilidad = model.EstadoDisponibilidad;

                await _context.SaveChangesAsync();
                TempData["EmpleadoDestacadoId"] = empleadoExistente.Id;

                await _auditService.LogAsync(userEmail, userRole, "Editar Empleado", "Empleados", 
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

                var cambios = new List<object>();
                void AgregarCambio(string campo, string anterior, string nuevo)
                {
                    if (!string.Equals(anterior, nuevo, StringComparison.Ordinal))
                    {
                        cambios.Add(new { Campo = campo, Anterior = anterior, Nuevo = nuevo });
                    }
                }

                AgregarCambio("Nombre completo", empleadoExistente.NombreCompleto, model.NombreCompleto);
                AgregarCambio("Correo electrónico", empleadoExistente.Correo ?? "-", model.Correo ?? "-");
                AgregarCambio("Fecha de ingreso", empleadoExistente.FechaIngreso.ToString("dd/MM/yyyy"), model.FechaIngreso.ToString("dd/MM/yyyy"));
                AgregarCambio("Hora de entrada", FormatHora(empleadoExistente.HorarioEntrada), FormatHora(model.HorarioEntrada));
                AgregarCambio("Hora de salida", FormatHora(empleadoExistente.HorarioSalida), FormatHora(model.HorarioSalida));
                AgregarCambio("Estado", empleadoExistente.EstadoDisponibilidad, model.EstadoDisponibilidad);

                var datosSolicitud = new
                {
                    Motivo = motivo,
                    Cambios = cambios,
                    NuevosDatos = model
                };

                var solicitud = new Solicitud
                {
                    TipoSolicitud = "EdicionEmpleado",
                    UsuarioSolicitanteId = userId,
                    Estado = "Pendiente",
                    FechaSolicitud = DateTime.Now,
                    DatosJson = System.Text.Json.JsonSerializer.Serialize(datosSolicitud)
                };

                _context.Solicitudes.Add(solicitud);
                _context.Notificaciones.Add(new Notificacion
                {
                    RolDestino = "Administrador",
                    Titulo = "Solicitud de Edición de Empleado",
                    Mensaje = $"El {User.DescripcionParaNotificacion()} solicitó modificar los datos del empleado {model.NombreCompleto}.",
                    Tipo = "Info",
                    FechaCreacion = DateTime.Now,
                    Enlace = "/Solicitudes/Index"
                });

                await _context.SaveChangesAsync();

                await _auditService.LogAsync(userEmail, userRole, "Solicitud Edición Empleado", "Solicitudes",
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

            var userEmail = User.FindFirstValue(ClaimTypes.Email) ?? "Usuario";
            var userRole = User.FindFirstValue(ClaimTypes.Role) ?? "Auxiliar";
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            // Administrador: Inactivación Directa (CU-12)
            if (userRole == "Administrador")
            {
                empleado.Activo = !empleado.Activo;
                empleado.EstadoDisponibilidad = empleado.Activo ? "Disponible" : "Inactivo";
                await _context.SaveChangesAsync();
                TempData["EmpleadoDestacadoId"] = empleado.Id;

                var accion = empleado.Activo ? "Reactivar Empleado" : "Inactivar Empleado";
                await _auditService.LogAsync(userEmail, userRole, accion, "Empleados", 
                    $"Estado de empleado {empleado.Identificacion} cambiado a {(empleado.Activo ? "Activo" : "Inactivo")}");

                TempData["SuccessMessage"] = $"El empleado '{empleado.NombreCompleto}' ahora está {(empleado.Activo ? "Activo" : "Inactivo")}.";
            }
            // Supervisor y Auxiliar: Solicitar Inactivación (CU-13)
            else
            {
                var solicitud = new Solicitud
                {
                    TipoSolicitud = "InactivacionEmpleado",
                    UsuarioSolicitanteId = userId,
                    Estado = "Pendiente",
                    FechaSolicitud = DateTime.Now,
                    DatosJson = System.Text.Json.JsonSerializer.Serialize(new
                    {
                        EmpleadoId = empleado.Id,
                        Identificacion = empleado.Identificacion,
                        NombreCompleto = empleado.NombreCompleto,
                        Motivo = motivo ?? "Solicitud de inactivación por novedades laborales"
                    })
                };

                _context.Solicitudes.Add(solicitud);
                _context.Notificaciones.Add(new Notificacion
                {
                    RolDestino = "Administrador",
                    Titulo = "Solicitud de Inactivación de Empleado",
                    Mensaje = $"El {User.DescripcionParaNotificacion()} solicitó inactivar al empleado {empleado.NombreCompleto}.",
                    Tipo = "Warning",
                    FechaCreacion = DateTime.Now,
                    Enlace = "/Solicitudes/Index"
                });

                await _context.SaveChangesAsync();

                await _auditService.LogAsync(userEmail, userRole, "Solicitud Inactivar Empleado", "Solicitudes", 
                    $"Solicitud de inactivación para {empleado.NombreCompleto}. Motivo: {motivo}");

                TempData["SuccessMessage"] = $"Se ha generado la solicitud de inactivación para '{empleado.NombreCompleto}'. Requiere aprobación del Administrador.";
            }

            return RedirectToAction(nameof(Index));
        }

        // Reiniciar el historial de tiempos usado para calcular el rendimiento del empleado (Solo Administrador).
        // No elimina ni desvincula los pedidos completados: solo deja de contarlos a partir de ahora.
        [Authorize(Roles = "Administrador")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ReiniciarPromedio(int id)
        {
            var empleado = await _context.Empleados.FindAsync(id);
            if (empleado == null) return NotFound();

            empleado.FechaReinicioPromedio = DateTime.Now;
            await _context.SaveChangesAsync();
            TempData["EmpleadoDestacadoId"] = empleado.Id;

            var userEmail = User.FindFirstValue(ClaimTypes.Email) ?? "Admin";
            await _auditService.LogAsync(userEmail, "Administrador", "Reiniciar Rendimiento", "Empleados",
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

        private static double CalcularDuracionJornadaHoras(TimeSpan entrada, TimeSpan salida)
        {
            var duracion = salida - entrada;
            if (duracion <= TimeSpan.Zero)
            {
                duracion += TimeSpan.FromHours(24);
            }
            return duracion.TotalHours;
        }

        private async Task<double> ObtenerMaxHorasJornadaAsync()
        {
            var cfg = await _context.ConfiguracionesSistema.FirstOrDefaultAsync(c => c.Clave == "MaxHorasJornada");
            return cfg != null && Formato.TryParseDecimal(cfg.Valor, out var horas) && horas > 0 ? horas : 9.0;
        }

        // Un correo no puede repetirse entre Empleados ni con ningún Usuario del sistema, aunque sean tablas distintas.
        private async Task<bool> CorreoDisponibleAsync(string correo, int? empleadoIdExcluir = null)
        {
            var enOtroEmpleado = await _context.Empleados.AnyAsync(e => e.Correo == correo && e.Id != (empleadoIdExcluir ?? 0));
            if (enOtroEmpleado) return false;
            return !await _context.Usuarios.AnyAsync(u => u.Correo == correo);
        }
    }
}
