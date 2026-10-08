using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickingApp.Data;
using PickingApp.Middleware;
using PickingApp.Models;
using PickingApp.Models.ViewModels;
using PickingApp.Services;

namespace PickingApp.Controllers
{
    // Flujo de aprobación: los cambios que un rol no puede hacer directamente quedan como Solicitud
    // y aquí el Administrador (o el Supervisor, solo para registros de empleado) los aprueba o rechaza
    [Authorize]
    public class SolicitudesController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IAuditService _auditService;
        private readonly ILogger<SolicitudesController> _logger;

        public SolicitudesController(ApplicationDbContext context, IAuditService auditService, ILogger<SolicitudesController> logger)
        {
            _context = context;
            _auditService = auditService;
            _logger = logger;
        }

        private const int TipoGestionableSupervisor = FlujoSolicitudes.TipoGestionableSupervisor;
        private const string MensajeSinPermisoSupervisor = "Los Supervisores solo tienen permiso para gestionar solicitudes de registro de empleados.";

        // Cabe en la notificación al solicitante (500 caracteres con el resto del mensaje) y en MotivoRechazo
        private const int MaxMotivoRechazo = 400;

        private bool PuedeGestionar(Solicitud solicitud) =>
            !User.IsInRole(Roles.Supervisor) || solicitud.TipoSolicitudId == TipoGestionableSupervisor;

        private int UsuarioActualId => User.IdUsuario();

        // Bandeja de aprobación para Administrador y Supervisor
        [Authorize(Roles = Roles.AdministradorOSupervisor)]
        public Task<IActionResult> Index(FiltrosSolicitudes filtros) => ListarAsync(esBandeja: true, filtros);

        // CU-17: Consultar estado de solicitudes propias (Auxiliar y Supervisor)
        public Task<IActionResult> MisSolicitudes(FiltrosSolicitudes filtros) => ListarAsync(esBandeja: false, filtros);

        // Las dos listas usan la misma vista y los mismos filtros; cambia qué solicitudes entran
        private async Task<IActionResult> ListarAsync(bool esBandeja, FiltrosSolicitudes filtros)
        {
            var esSupervisor = User.IsInRole(Roles.Supervisor);
            var userId = UsuarioActualId;

            var baseQuery = _context.Solicitudes.AsQueryable();
            if (!esBandeja)
            {
                baseQuery = baseQuery.Where(s => s.UsuarioSolicitanteId == userId);
            }
            else if (esSupervisor)
            {
                // Supervisor solo puede aprobar/rechazar solicitudes de Registro de Empleado (CU-08)
                baseQuery = baseQuery.Where(s => s.TipoSolicitudId == TipoGestionableSupervisor);
            }

            // Opciones de los filtros: lo que existe en esta lista, antes de filtrar
            var idsTipos = await baseQuery.Select(s => s.TipoSolicitudId).Distinct().ToListAsync();
            var idsSolicitantes = esBandeja
                ? await baseQuery.Select(s => s.UsuarioSolicitanteId).Distinct().ToListAsync()
                : new List<int>();
            var solicitantes = await _context.Usuarios
                .Where(u => idsSolicitantes.Contains(u.Id))
                .OrderBy(u => u.NombreCompleto)
                .ToListAsync();

            var query = baseQuery
                .Include(s => s.UsuarioSolicitante)
                .Include(s => s.UsuarioResponde)
                .AsQueryable();

            // "SOL-12" o "12" buscan la solicitud número 12
            var digitos = new string((filtros.Busqueda ?? string.Empty).Where(char.IsDigit).ToArray());
            if (int.TryParse(digitos, out var numero))
            {
                query = query.Where(s => s.Id == numero);
            }
            if (filtros.Tipo.HasValue)
            {
                query = query.Where(s => s.TipoSolicitudId == filtros.Tipo.Value);
            }
            if (!string.IsNullOrEmpty(filtros.Estado))
            {
                query = query.Where(s => s.Estado == filtros.Estado);
            }
            if (esBandeja && filtros.SolicitanteId.HasValue)
            {
                query = query.Where(s => s.UsuarioSolicitanteId == filtros.SolicitanteId.Value);
            }
            if (filtros.FechaDesde.HasValue)
            {
                var desde = filtros.FechaDesde.Value.Date;
                query = query.Where(s => s.FechaSolicitud >= desde);
            }
            if (filtros.FechaHasta.HasValue)
            {
                var hastaExclusivo = filtros.FechaHasta.Value.Date.AddDays(1);
                query = query.Where(s => s.FechaSolicitud < hastaExclusivo);
            }

            var modelo = new SolicitudesListaViewModel
            {
                // Pendientes primero; dentro de cada grupo, las más recientes
                Solicitudes = await query
                    .OrderByDescending(s => s.Estado == EstadosSolicitud.Pendiente)
                    .ThenByDescending(s => s.FechaSolicitud)
                    .ToListAsync(),
                Filtros = filtros,
                EsBandeja = esBandeja,
                MostrarPestanas = esSupervisor,
                PendientesPorRevisar = esSupervisor
                    ? await _context.Solicitudes.CountAsync(s => s.Estado == EstadosSolicitud.Pendiente && s.TipoSolicitudId == TipoGestionableSupervisor)
                    : 0,
                Tipos = await _context.CatalogoTiposSolicitud
                    .Where(t => idsTipos.Contains(t.Id))
                    .OrderBy(t => t.Nombre)
                    .ToListAsync(),
                Solicitantes = solicitantes
            };
            return View("Index", modelo);
        }

        // CU-08: Revisar Solicitud
        [Authorize(Roles = Roles.AdministradorOSupervisor)]
        [HttpGet]
        public async Task<IActionResult> Revisar(int id)
        {
            var solicitud = await BuscarAsync(id);
            if (solicitud == null) return NotFound();

            if (!PuedeGestionar(solicitud))
            {
                // Una solicitud propia del Supervisor se ve en modo consulta
                if (solicitud.UsuarioSolicitanteId == UsuarioActualId) return RedirectToAction(nameof(Detalle), new { id });

                TempData["ErrorMessage"] = MensajeSinPermisoSupervisor;
                return RedirectToAction(nameof(Index));
            }

            var modelo = await ConstruirDetalleAsync(solicitud);
            modelo.PuedeResponder = solicitud.Estado == EstadosSolicitud.Pendiente;
            return View(modelo);
        }

        // Consulta de una solicitud propia (Auxiliar y Supervisor): datos, estado y, si la rechazaron, el motivo
        [HttpGet]
        public async Task<IActionResult> Detalle(int id)
        {
            var solicitud = await BuscarAsync(id);
            if (solicitud == null) return NotFound();

            if (solicitud.UsuarioSolicitanteId != UsuarioActualId)
            {
                // Quien puede gestionarla la abre en la bandeja; los demás no ven solicitudes ajenas
                if (User.IsInRole(Roles.Administrador) || (User.IsInRole(Roles.Supervisor) && PuedeGestionar(solicitud)))
                {
                    return RedirectToAction(nameof(Revisar), new { id });
                }
                return NotFound();
            }

            var modelo = await ConstruirDetalleAsync(solicitud);
            modelo.AccionVolver = nameof(MisSolicitudes);
            return View("Revisar", modelo);
        }

        private Task<Solicitud?> BuscarAsync(int id) => _context.Solicitudes
            .Include(s => s.UsuarioSolicitante)
            .Include(s => s.UsuarioResponde)
            .FirstOrDefaultAsync(s => s.Id == id);

        // Qué se muestra de cada tipo de solicitud. Los datos de hoy (empleado, ubicación, bodega, producto)
        // se leen de la base de datos; si ya no existen, se usan los que se guardaron al crear la solicitud.
        private async Task<SolicitudDetalleViewModel> ConstruirDetalleAsync(Solicitud solicitud)
        {
            var modelo = new SolicitudDetalleViewModel { Solicitud = solicitud };
            var pendiente = solicitud.Estado == EstadosSolicitud.Pendiente;

            switch (solicitud.TipoSolicitudId)
            {
                case TiposSolicitud.RegistroEmpleado:
                {
                    var registro = solicitud.LeerDatos<DatosEmpleadoSolicitud>();
                    modelo.TituloDatos = "Empleado a registrar";
                    modelo.Datos.Add(("Número de documento", registro.Identificacion));
                    modelo.Datos.Add(("Nombre completo", registro.NombreCompleto));
                    modelo.Datos.Add(("Correo electrónico", registro.Correo ?? "Sin correo"));
                    modelo.Datos.Add(("Fecha de ingreso", registro.FechaIngreso.ToString("dd/MM/yyyy")));
                    modelo.Datos.Add(("Horario", $"{Hora(registro.HorarioEntrada)} - {Hora(registro.HorarioSalida)}"));
                    break;
                }
                case TiposSolicitud.EdicionEmpleado:
                {
                    var edicion = solicitud.LeerDatos<SolicitudEdicionEmpleado>();
                    var empleado = await _context.Empleados.FindAsync(edicion.NuevosDatos.Id);
                    // El nombre "de antes" está en los cambios si se pidió cambiarlo
                    var nombre = empleado?.NombreCompleto
                        ?? edicion.Cambios.FirstOrDefault(c => c.Campo == CamposEmpleado.Nombre)?.Anterior
                        ?? edicion.NuevosDatos.NombreCompleto;
                    modelo.TituloDatos = "Empleado";
                    modelo.Datos.Add(("Nombre completo", nombre));
                    modelo.Datos.Add(("Número de documento", empleado?.Identificacion ?? edicion.NuevosDatos.Identificacion));
                    if (empleado != null) modelo.Datos.Add(("Estado", empleado.Activo ? "Activo" : "Inactivo"));
                    else if (pendiente) modelo.Aviso = "El empleado ya no existe: no se puede aprobar. Rechace la solicitud.";

                    modelo.EsEdicion = true;
                    modelo.Motivo = edicion.Motivo;
                    modelo.Cambios = edicion.Cambios.Select(c => Comparar(c, pendiente && empleado != null ? ValorActual(empleado, c.Campo) : null)).ToList();
                    break;
                }
                case TiposSolicitud.InactivacionEmpleado:
                {
                    var inactivacion = solicitud.LeerDatos<SolicitudInactivacionEmpleado>();
                    var empleado = await _context.Empleados.FindAsync(inactivacion.EmpleadoId);
                    modelo.TituloDatos = "Empleado a inactivar";
                    modelo.Datos.Add(("Nombre completo", empleado?.NombreCompleto ?? inactivacion.NombreCompleto));
                    modelo.Datos.Add(("Número de documento", empleado?.Identificacion ?? inactivacion.Identificacion));
                    if (empleado != null) modelo.Datos.Add(("Estado actual", empleado.Activo ? "Activo" : "Inactivo"));
                    modelo.Motivo = inactivacion.Motivo;

                    if (pendiente)
                    {
                        var pedidoEnCurso = empleado == null ? null : await _context.Pedidos
                            .Where(p => p.EmpleadoId == empleado.Id && PedidoEstados.EnCurso.Contains(p.Estado))
                            .Select(p => p.CodigoPedido)
                            .FirstOrDefaultAsync();
                        modelo.Aviso = empleado == null ? "El empleado ya no existe: no se puede aprobar. Rechace la solicitud."
                            : !empleado.Activo ? "El empleado ya está inactivo."
                            : pedidoEnCurso != null ? $"Tiene el pedido {pedidoEnCurso} en curso: no se podrá aprobar hasta que se reasigne, complete o cancele."
                            : null;
                    }
                    break;
                }
                case TiposSolicitud.EdicionUbicacion:
                {
                    var edicion = solicitud.LeerDatos<SolicitudEdicionUbicacion>();
                    var ubicacion = await UbicacionConDatosAsync(edicion.UbicacionId);
                    modelo.TituloDatos = "Ubicación";
                    AgregarDatosUbicacion(modelo, ubicacion,
                        edicion.CodigoUbicacion ?? edicion.Cambios.FirstOrDefault(c => c.Campo == CamposUbicacion.Codigo)?.Anterior,
                        edicion.BodegaCodigo, edicion.Producto ?? edicion.Cambios.FirstOrDefault(c => c.Campo == CamposUbicacion.Producto)?.Anterior);
                    if (ubicacion == null && pendiente) modelo.Aviso = "La ubicación ya no existe: no se puede aprobar. Rechace la solicitud.";

                    modelo.EsEdicion = true;
                    modelo.Motivo = edicion.Motivo;
                    modelo.Cambios = edicion.Cambios.Select(c => Comparar(c, pendiente && ubicacion != null ? ValorActual(ubicacion, c.Campo) : null)).ToList();
                    break;
                }
                case TiposSolicitud.EliminacionUbicacion:
                {
                    var eliminacion = solicitud.LeerDatos<SolicitudEliminacionUbicacion>();
                    var ubicacion = await UbicacionConDatosAsync(eliminacion.UbicacionId);
                    modelo.TituloDatos = "Ubicación a eliminar";
                    AgregarDatosUbicacion(modelo, ubicacion, eliminacion.CodigoUbicacion, eliminacion.BodegaCodigo, eliminacion.Producto);
                    modelo.Motivo = eliminacion.Motivo;

                    if (pendiente)
                    {
                        modelo.Aviso = ubicacion == null ? "La ubicación ya no existe: no se puede aprobar. Rechace la solicitud."
                            : await _context.DetallesPedido.AnyAsync(d => d.UbicacionId == ubicacion.Id)
                                ? "La ubicación tiene pedidos asociados: no se puede eliminar. Rechace la solicitud."
                                : null;
                    }
                    break;
                }
                default:
                    modelo.TituloDatos = "Datos";
                    break;
            }

            if (string.IsNullOrWhiteSpace(modelo.Motivo) && solicitud.TipoSolicitudId != TiposSolicitud.RegistroEmpleado)
            {
                modelo.Motivo = "No especificado";
            }
            return modelo;
        }

        private Task<Ubicacion?> UbicacionConDatosAsync(int id) => _context.Ubicaciones
            .Include(u => u.Bodega)
            .Include(u => u.Producto)
            .FirstOrDefaultAsync(u => u.Id == id);

        private static void AgregarDatosUbicacion(SolicitudDetalleViewModel modelo, Ubicacion? ubicacion,
            string? codigoGuardado, string? bodegaGuardada, string? productoGuardado)
        {
            modelo.Datos.Add(("Código", ubicacion?.CodigoUbicacion ?? codigoGuardado ?? "No disponible"));
            modelo.Datos.Add(("Bodega", ubicacion?.Bodega != null ? $"{ubicacion.Bodega.Codigo} · {ubicacion.Bodega.Nombre}" : bodegaGuardada ?? "No disponible"));
            modelo.Datos.Add(("Producto", ubicacion != null ? TextoProducto(ubicacion.Producto) : productoGuardado ?? "No disponible"));
            if (ubicacion != null) modelo.Datos.Add(("Estado", ubicacion.Activa ? "Activa" : "Inactiva"));
        }

        private static string TextoProducto(Producto? producto) =>
            producto != null ? $"{producto.Codigo} · {producto.Nombre}" : "Sin producto";

        private static string Hora(TimeSpan hora) => hora.ToString(@"hh\:mm");

        // Valor de hoy con el mismo formato que usó quien creó la solicitud; null si el campo no se compara
        private static string? ValorActual(Empleado empleado, string campo) => campo switch
        {
            CamposEmpleado.Nombre => empleado.NombreCompleto,
            CamposEmpleado.Correo => empleado.Correo ?? "-",
            CamposEmpleado.FechaIngreso => empleado.FechaIngreso.ToString("dd/MM/yyyy"),
            CamposEmpleado.HoraEntrada => Hora(empleado.HorarioEntrada),
            CamposEmpleado.HoraSalida => Hora(empleado.HorarioSalida),
            _ => null
        };

        private static string? ValorActual(Ubicacion ubicacion, string campo) => campo switch
        {
            CamposUbicacion.Codigo => ubicacion.CodigoUbicacion,
            CamposUbicacion.Estado => ubicacion.Activa ? "Activa" : "Inactiva",
            CamposUbicacion.Producto => ubicacion.Producto != null ? TextoProducto(ubicacion.Producto) : "-",
            _ => null
        };

        private static CambioDetalle Comparar(CambioSolicitud cambio, string? actual) => new()
        {
            Campo = cambio.Campo,
            Anterior = cambio.Anterior,
            Nuevo = cambio.Nuevo,
            Actual = actual != null && actual != cambio.Anterior ? actual : null
        };

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

            if (!PuedeGestionar(solicitud))
            {
                TempData["ErrorMessage"] = MensajeSinPermisoSupervisor;
                return RedirectToAction(nameof(Index));
            }

            try
            {
                // Aplica el cambio pedido (crear, editar o inactivar empleado; editar o eliminar ubicación)
                await FlujoSolicitudes.AplicarAsync(_context, solicitud);

                solicitud.Estado = EstadosSolicitud.Aprobada;
                solicitud.FechaRespuesta = DateTime.Now;
                solicitud.UsuarioRespondeId = UsuarioActualId;

                // Notificar al usuario solicitante (CU-43); el enlace abre esta solicitud
                Avisos.AlUsuario(_context, solicitud.UsuarioSolicitanteId,
                    "Solicitud Aprobada",
                    $"Tu solicitud SOL-{solicitud.Id} \"{solicitud.NombreTipo}\" ha sido aprobada por el {User.DescripcionParaNotificacion()}.",
                    TiposNotificacion.Success,
                    $"/Solicitudes/Detalle/{solicitud.Id}");

                // Un solo SaveChanges guarda el cambio aplicado, la solicitud aprobada y la notificación:
                // si algo falla, no se guarda nada a medias
                await _context.SaveChangesAsync();

                await _auditService.RegistrarAsync(AccionesLog.AprobarSolicitud, "Solicitudes",
                    $"Solicitud #{solicitud.Id} ({solicitud.NombreTipo}) aprobada.");

                TempData["SuccessMessage"] = $"Solicitud SOL-{solicitud.Id} aprobada correctamente.";
                return RedirectToAction(nameof(Index));
            }
            catch (ReglaNegocioException ex)
            {
                // La solicitud queda Pendiente: se puede aprobar cuando se resuelva el impedimento, o rechazar.
                TempData["ErrorMessage"] = ex.Message;
            }
            // Los errores de base de datos siguen hasta ErrorBaseDatosMiddleware, que muestra el aviso comprensible
            catch (Exception ex) when (!ErroresBaseDatos.EsErrorDeBaseDatos(ex))
            {
                _logger.LogError(ex, "Error inesperado al aprobar la solicitud {Id}.", id);
                TempData["ErrorMessage"] = "No se pudo aprobar la solicitud por un error inesperado; no se guardó ningún cambio. Inténtelo de nuevo.";
            }

            // Si no se pudo aprobar, se vuelve a la misma solicitud para rechazarla o revisarla
            return RedirectToAction(nameof(Revisar), new { id });
        }

        // Rechazar Solicitud (CU-08, CU-11, CU-14, CU-21, CU-41)
        [Authorize(Roles = Roles.AdministradorOSupervisor)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Rechazar(int id, string? motivoRechazo)
        {
            motivoRechazo = (motivoRechazo ?? string.Empty).Trim();
            if (motivoRechazo.Length == 0 || motivoRechazo.Length > MaxMotivoRechazo)
            {
                TempData["ErrorMessage"] = motivoRechazo.Length == 0
                    ? "Para rechazar la solicitud escriba el motivo del rechazo: el solicitante lo verá."
                    : $"El motivo del rechazo admite hasta {MaxMotivoRechazo} caracteres.";
                return RedirectToAction(nameof(Revisar), new { id });
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

            solicitud.Estado = EstadosSolicitud.Rechazada;
            solicitud.MotivoRechazo = motivoRechazo;
            solicitud.FechaRespuesta = DateTime.Now;
            solicitud.UsuarioRespondeId = UsuarioActualId;

            // Notificar al usuario solicitante (CU-43); el enlace abre la solicitud con el motivo
            Avisos.AlUsuario(_context, solicitud.UsuarioSolicitanteId,
                "Solicitud Rechazada",
                $"Tu solicitud SOL-{solicitud.Id} \"{solicitud.NombreTipo}\" fue rechazada. Motivo: {motivoRechazo}",
                TiposNotificacion.Danger,
                $"/Solicitudes/Detalle/{solicitud.Id}");

            await _context.SaveChangesAsync();

            await _auditService.RegistrarAsync(AccionesLog.RechazarSolicitud, "Solicitudes",
                $"Solicitud #{solicitud.Id} rechazada. Motivo: {motivoRechazo}");

            TempData["SuccessMessage"] = $"Solicitud SOL-{solicitud.Id} rechazada.";
            return RedirectToAction(nameof(Index));
        }
    }
}
