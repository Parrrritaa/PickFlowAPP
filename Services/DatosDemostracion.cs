using Microsoft.EntityFrameworkCore;
using PickingApp.Data;
using PickingApp.Models;

namespace PickingApp.Services
{
    // Lo que se creó al cargar los datos de demostración (para el mensaje y la auditoría)
    public record ResumenDemostracion(int Pedidos, int Completados, int Cancelados, int EnCurso, int Pendientes,
        int Novedades, int Solicitudes, List<string> Bodegas);

    // Simula unos días de uso del sistema para mostrarlo funcionando (Configuración → Datos de demostración).
    // Las cantidades, empleados, productos y tiempos son aleatorios, pero siempre aparece cada caso: pedidos pendientes,
    // en curso, reasignados, completados, cancelados y divididos, novedades abiertas y atendidas, y solicitudes
    // pendientes, aprobadas y rechazadas. Las fechas van hacia atrás desde el momento en que se pulsa el botón.
    //
    // No pasa por los controladores (sus acciones usan la hora actual), pero aplica sus mismas reglas: un pedido en
    // curso por empleado, asignaciones dentro de la jornada y desde la fecha de ingreso, tiempo real desde el inicio
    // del picking (asignación + margen), partes que cierran el pedido original y el mismo texto en el LOG.
    // El paso a paso de cada día está en SimulacionDemostracion.
    public class DatosDemostracion
    {
        public const int DiasSimulados = 6;
        public const int MinimoEmpleados = 2;
        public const int MinimoUbicaciones = 3;

        internal static readonly string[] MotivosCancelacion =
        {
            "El cliente canceló la orden.",
            "Pedido duplicado en el sistema.",
            "Dirección de entrega errada; el cliente enviará un pedido nuevo.",
            "Producto agotado en bodega."
        };

        internal static readonly string[] MotivosReasignacion =
        {
            "El empleado salió a su hora de almuerzo.",
            "El empleado pasó a apoyar la recepción de mercancía.",
            "Calamidad doméstica del empleado.",
            "El empleado terminó su turno."
        };

        internal static readonly (string Tipo, string Descripcion, string Atencion)[] Novedades =
        {
            (TiposNovedad.Faltante, "Faltan unidades en la ubicación; se recogió lo disponible.", "Se completó con stock de reserva."),
            (TiposNovedad.Averia, "Una caja llegó con el empaque roto.", "Se reemplazó la caja y se reportó a calidad."),
            (TiposNovedad.UbicacionIncorrecta, "El producto estaba en el nivel de abajo, no en el indicado.", "Se reubicó el producto y se corrigió la ubicación."),
            (TiposNovedad.Otro, "El montacargas del pasillo estaba ocupado y se esperó para recoger.", "Se coordinó el turno del montacargas.")
        };

        private static readonly string[] NombresAspirantes =
        {
            "Valentina Ríos Ospina", "Andrés Felipe Cárdenas", "Daniela Moreno Peña", "Juan Sebastián Lozano", "Paula Andrea Quintero"
        };

        private readonly ApplicationDbContext _context;
        private readonly IPickingEngine _pickingEngine;
        private readonly Random _azar = new();

        public DatosDemostracion(ApplicationDbContext context, IPickingEngine pickingEngine)
        {
            _context = context;
            _pickingEngine = pickingEngine;
        }

        // null si se pueden cargar; si no, qué falta. Se exige el historial vacío para no mezclarlos con pedidos reales.
        public async Task<string?> MotivoNoDisponibleAsync()
        {
            if (await _context.Pedidos.AnyAsync())
                return "Ya hay pedidos registrados. Borre primero el historial de pedidos para cargar la demostración desde cero.";
            if (await _context.Empleados.CountAsync(e => e.Activo) < MinimoEmpleados)
                return $"Se necesitan al menos {MinimoEmpleados} empleados activos.";
            if ((await UbicacionesPorBodegaAsync()).Sum(b => b.Count) < MinimoUbicaciones)
                return $"Se necesitan al menos {MinimoUbicaciones} ubicaciones activas con producto en bodegas activas.";
            return null;
        }

        // Ubicaciones de donde pueden salir pedidos, agrupadas por bodega: toda bodega activa con al menos una
        // ubicación activa que tenga producto recibe pedidos (las que tienen pocas, pedidos de pocas líneas)
        private async Task<List<List<Ubicacion>>> UbicacionesPorBodegaAsync()
        {
            var ubicaciones = await _context.Ubicaciones
                .Include(u => u.Producto)
                .Include(u => u.Bodega)
                .Where(u => u.Activa && u.Producto != null && u.Bodega!.Activa)
                .ToListAsync();
            return ubicaciones.GroupBy(u => u.BodegaId)
                .Select(g => g.ToList())
                .ToList();
        }

        public async Task<ResumenDemostracion> GenerarAsync(int administradorId)
        {
            var usuarios = await _context.Usuarios.Include(u => u.Rol)
                .Where(u => u.Estado == EstadosUsuario.Activo || u.Id == administradorId).ToListAsync();
            var administrador = usuarios.First(u => u.Id == administradorId);
            // El trabajo diario lo hace el Supervisor; sin Supervisor activo, el Administrador
            var supervisor = usuarios.FirstOrDefault(u => u.RolId == Roles.IdSupervisor) ?? administrador;
            var auxiliar = usuarios.FirstOrDefault(u => u.RolId == Roles.IdAuxiliar);

            var sim = new SimulacionDemostracion(DateTime.Now, _azar,
                await _context.Empleados.Where(e => e.Activo).ToListAsync(),
                await ParametrosSistema.ObtenerAsync(_context, ParametrosSistema.MargenInicioMinutos),
                supervisor, administrador);

            // Todo o nada: si algo falla, no queda una demostración a medias
            await using var transaccion = await _context.Database.BeginTransactionAsync();

            // 1. Pedidos de cada día; se guardan primero para tener su Id y calcular su tiempo estándar
            var bodegas = await UbicacionesPorBodegaAsync();
            var pedidos = CrearPedidos(sim, bodegas);
            _context.Pedidos.AddRange(pedidos.Where(p => p.PedidoPadre == null)); // las partes entran con su pedido original
            await _context.SaveChangesAsync();

            var trabajo = pedidos.Where(p => !p.EstaDividido).ToList();
            var divididos = pedidos.Where(p => p.EstaDividido).ToList();
            var estandar = await _pickingEngine.CalcularTiemposEstandarAsync(trabajo.Select(p => p.Id).ToList());

            // 2. Día por día: asignar, reasignar, completar o cancelar
            sim.Ejecutar(trabajo, estandar);
            sim.Garantizar(trabajo, estandar);
            foreach (var padre in divididos.Where(p => p.CerrarSiTerminaronLasPartes()))
            {
                var completado = padre.Estado == PedidoEstados.Completado;
                sim.Log(padre.FechaFinalizacion!.Value, supervisor, completado ? "Completar Pedido" : "Cancelar Pedido",
                    $"Pedido {padre.CodigoPedido} {(completado ? "completado" : "cancelado")} automáticamente: finalizaron sus {padre.Partes.Count} partes.");
            }

            // 3. Novedades y solicitudes; su LOG y sus avisos llevan el número que les da la base de datos
            var novedades = sim.CrearNovedades(trabajo);
            _context.NovedadesPedido.AddRange(novedades);
            var solicitudes = await CrearSolicitudesAsync(sim, supervisor, administrador, auxiliar);
            await _context.SaveChangesAsync();

            sim.LogAtenciones(novedades);
            solicitudes.ForEach(s => s.Avisar(sim));

            _context.Notificaciones.AddRange(sim.Notificaciones);
            if (await ParametrosSistema.LogHabilitadoAsync(_context))
                _context.LogsSistema.AddRange(sim.Logs);
            await _context.SaveChangesAsync();
            await transaccion.CommitAsync();

            return new ResumenDemostracion(
                Pedidos: pedidos.Count,
                Completados: trabajo.Count(p => p.Estado == PedidoEstados.Completado),
                Cancelados: trabajo.Count(p => p.Estado == PedidoEstados.Cancelado),
                EnCurso: trabajo.Count(p => p.EstaEnCurso),
                Pendientes: trabajo.Count(p => p.Estado == PedidoEstados.Pendiente),
                Novedades: novedades.Count,
                Solicitudes: solicitudes.Count,
                Bodegas: bodegas.Select(b => b[0].Bodega!.Nombre).OrderBy(n => n).ToList());
        }

        // Una o dos cargas de Excel por día (las de hoy, solo si ya pasaron) y, al final, una carga reciente que queda
        // pendiente. Cada pedido sale de una sola bodega, como en la carga real.
        private List<Pedido> CrearPedidos(SimulacionDemostracion sim, List<List<Ubicacion>> bodegas)
        {
            var pedidos = new List<Pedido>();
            var hoy = sim.Ahora.Date;
            Pedido? grande = null;
            var numero = 1; // consecutivo del año, como los códigos de los Excel reales: PED-2026-001

            for (var d = DiasSimulados - 1; d >= 0; d--)
            {
                var dia = hoy.AddDays(-d);
                var cantidad = dia.DayOfWeek == DayOfWeek.Sunday ? _azar.Next(3, 6) : d == 0 ? _azar.Next(6, 10) : _azar.Next(8, 14);

                var cargas = new List<DateTime> { dia.Add(new TimeSpan(7, _azar.Next(5, 50), 0)) };
                if (_azar.NextDouble() < 0.6) cargas.Add(dia.Add(new TimeSpan(_azar.Next(10, 13), _azar.Next(0, 60), 0)));
                if (d == 0)
                {
                    // Si todavía es muy temprano, la carga de hoy fue hace un rato
                    cargas = cargas.Where(c => c < sim.Ahora.AddMinutes(-40)).ToList();
                    if (cargas.Count == 0) cargas.Add(Maximo(dia, sim.Ahora.AddMinutes(-90)));
                }

                for (var c = 0; c < cargas.Count; c++)
                {
                    var enEstaCarga = c == cargas.Count - 1 ? cantidad - (cantidad / cargas.Count) * c : cantidad / cargas.Count;
                    var lote = new List<Pedido>();
                    for (var i = 0; i < enEstaCarga; i++)
                    {
                        var esGrande = d == 0 && grande == null;
                        var pedido = NuevoPedido(numero++, cargas[c], bodegas, esGrande);
                        if (esGrande) grande = pedido;
                        lote.Add(pedido);
                    }
                    pedidos.AddRange(lote);
                    sim.LogCarga(cargas[c], lote);
                }

                if (d == 0)
                {
                    // Carga reciente: queda pendiente para mostrar la asignación en vivo
                    var reciente = Maximo(cargas.Max().AddMinutes(5), sim.Ahora.AddMinutes(-_azar.Next(8, 25)));
                    var lote = Enumerable.Range(0, _azar.Next(4, 6))
                        .Select(_ => NuevoPedido(numero++, reciente, bodegas, grande: false))
                        .ToList();
                    lote.ForEach(p => sim.SinSimular.Add(p));
                    pedidos.AddRange(lote);
                    sim.LogCarga(reciente, lote);
                }
            }

            // El pedido grande de hoy se divide en partes, como se hace con uno que no cabe en una jornada
            pedidos.AddRange(Dividir(grande!, sim));
            return pedidos;
        }

        private Pedido NuevoPedido(int numero, DateTime carga, List<List<Ubicacion>> bodegas, bool grande)
        {
            var bodega = bodegas[_azar.Next(bodegas.Count)];
            var lineas = Math.Min(bodega.Count, grande ? _azar.Next(6, 11) : _azar.NextDouble() < 0.7 ? _azar.Next(1, 4) : _azar.Next(4, 6));
            var prioridad = _azar.NextDouble() switch { < 0.25 => Prioridades.Alta, < 0.75 => Prioridades.Media, _ => Prioridades.Baja };

            return new Pedido
            {
                CodigoPedido = $"PED-{carga:yyyy}-{numero:000}",
                BodegaId = bodega[0].BodegaId,
                Prioridad = prioridad,
                Estado = PedidoEstados.Pendiente,
                FechaCarga = carga,
                Detalles = bodega.OrderBy(_ => _azar.Next()).Take(lineas).Select(u => new DetallePedido
                {
                    CodigoProducto = u.Producto!.Codigo,
                    NombreProducto = u.Producto.Nombre,
                    UbicacionId = u.Id,
                    // Los productos que vienen por caja se piden casi siempre en cajas completas
                    Cantidad = u.Producto.UnidadesPorCaja > 1 && _azar.NextDouble() < 0.6
                        ? u.Producto.UnidadesPorCaja * _azar.Next(1, 4)
                        : grande ? _azar.Next(4, 25) : _azar.Next(1, 13)
                }).ToList()
            };
        }

        // Reparte las líneas en 2 o 3 partes (PED-XXX-P1, P2...), igual que PedidosController.Dividir.
        // Con una sola línea se reparten sus unidades.
        private List<Pedido> Dividir(Pedido pedido, SimulacionDemostracion sim)
        {
            var lineas = pedido.Detalles.ToList();
            var totalPartes = lineas.Count == 1 ? 2 : Math.Min(lineas.Count, _azar.Next(2, 4));
            var partes = Enumerable.Range(1, totalPartes).Select(k => new Pedido
            {
                CodigoPedido = $"{pedido.CodigoPedido}-P{k}",
                BodegaId = pedido.BodegaId,
                Prioridad = pedido.Prioridad,
                Estado = PedidoEstados.Pendiente,
                FechaCarga = pedido.FechaCarga,
                NumeroParte = k,
                PedidoPadre = pedido
            }).ToList();

            DetallePedido Copia(DetallePedido l, int cantidad) => new()
            {
                CodigoProducto = l.CodigoProducto, NombreProducto = l.NombreProducto, Cantidad = cantidad, UbicacionId = l.UbicacionId
            };

            if (lineas.Count == 1)
            {
                var mitad = lineas[0].Cantidad / 2;
                partes[0].Detalles.Add(Copia(lineas[0], lineas[0].Cantidad - mitad));
                partes[1].Detalles.Add(Copia(lineas[0], mitad));
            }
            else
            {
                for (var i = 0; i < lineas.Count; i++) partes[i % totalPartes].Detalles.Add(Copia(lineas[i], lineas[i].Cantidad));
            }

            pedido.Estado = PedidoEstados.Dividido;
            partes.ForEach(pedido.Partes.Add);
            sim.Log(pedido.FechaCarga.AddMinutes(12), sim.Supervisor, "Dividir Pedido",
                $"Pedido {pedido.CodigoPedido} dividido en {totalPartes} partes: " +
                string.Join(", ", partes.Select(p => $"{p.CodigoPedido} ({p.Detalles.Sum(d => d.Cantidad)} uds)")) + ".");
            return partes;
        }

        // Una solicitud de la demostración con lo necesario para su aviso y su LOG, que se escriben cuando ya tiene Id
        private sealed record SolicitudDemo(Solicitud Solicitud, Usuario Solicitante, int RolDestinoId, string Titulo, string TipoAviso,
            string QueSolicito, string LogAccion, string LogDetalle, Usuario? Responde = null)
        {
            public void Avisar(SimulacionDemostracion sim)
            {
                var s = Solicitud;
                var nombreTipo = TiposSolicitud.Catalogo.First(t => t.Id == s.TipoSolicitudId).Nombre;
                var respondida = s.Estado != EstadosSolicitud.Pendiente;

                sim.Log(s.FechaSolicitud, Solicitante, LogAccion, LogDetalle, "Solicitudes");
                sim.Notificar(s.FechaSolicitud, Titulo,
                    $"El {ClaimsPrincipalExtensions.DescribirUsuario(Solicitante.NombreCompleto, Solicitante.NombreRol)} solicitó {QueSolicito}.",
                    TipoAviso, "/Solicitudes/Index", rolDestinoId: RolDestinoId, leida: respondida);
                if (!respondida || Responde == null) return;

                var fecha = s.FechaRespuesta!.Value;
                var quien = ClaimsPrincipalExtensions.DescribirUsuario(Responde.NombreCompleto, Responde.NombreRol);
                if (s.Estado == EstadosSolicitud.Aprobada)
                {
                    sim.Notificar(fecha, "Solicitud Aprobada", $"Tu solicitud SOL-{s.Id} \"{nombreTipo}\" ha sido aprobada por el {quien}.",
                        TiposNotificacion.Success, $"/Solicitudes/Detalle/{s.Id}", usuarioDestinoId: Solicitante.Id);
                    sim.Log(fecha, Responde, "Aprobar Solicitud", $"Solicitud #{s.Id} ({nombreTipo}) aprobada.", "Solicitudes");
                }
                else
                {
                    sim.Notificar(fecha, "Solicitud Rechazada", $"Tu solicitud SOL-{s.Id} \"{nombreTipo}\" fue rechazada. Motivo: {s.MotivoRechazo}",
                        TiposNotificacion.Danger, $"/Solicitudes/Detalle/{s.Id}", usuarioDestinoId: Solicitante.Id);
                    sim.Log(fecha, Responde, "Rechazar Solicitud", $"Solicitud #{s.Id} rechazada. Motivo: {s.MotivoRechazo}", "Solicitudes");
                }
            }
        }

        // Hasta cinco solicitudes con los tres estados. Las aprobadas describen cambios que ya coinciden con los datos
        // actuales y las rechazadas no cambian nada: no se crea ni modifica ningún empleado. Se omiten las que no
        // tienen quién las envíe (sin Supervisor o sin Auxiliar) y no se repite una pendiente sobre el mismo empleado.
        private async Task<List<SolicitudDemo>> CrearSolicitudesAsync(SimulacionDemostracion sim, Usuario supervisor, Usuario administrador, Usuario? auxiliar)
        {
            var hoy = sim.Ahora.Date;
            var creadas = new List<SolicitudDemo>();

            SolicitudDemo Agregar(int tipo, Usuario solicitante, object datos, DateTime fecha, int rolDestinoId, string titulo, string tipoAviso,
                string queSolicito, string logAccion, string logDetalle, Usuario? responde = null, string? motivoRechazo = null)
            {
                var s = Solicitud.Nueva(tipo, solicitante.Id, datos);
                s.FechaSolicitud = fecha;
                if (responde != null)
                {
                    s.Estado = motivoRechazo == null ? EstadosSolicitud.Aprobada : EstadosSolicitud.Rechazada;
                    s.MotivoRechazo = motivoRechazo;
                    s.UsuarioRespondeId = responde.Id;
                    s.FechaRespuesta = Minimo(fecha.AddHours(_azar.Next(2, 20)), sim.Ahora);
                }
                _context.Solicitudes.Add(s);
                var demo = new SolicitudDemo(s, solicitante, rolDestinoId, titulo, tipoAviso, queSolicito, logAccion, logDetalle, responde);
                creadas.Add(demo);
                return demo;
            }

            var libres = new List<Empleado>();
            foreach (var e in (await _context.Empleados.Where(e => e.Activo).ToListAsync()).OrderBy(_ => _azar.Next()))
                if (await SolicitudesPendientes.DeEmpleadoAsync(_context, e.Id) == null) libres.Add(e);

            if (supervisor.Id != administrador.Id)
            {
                if (libres.Count > 0)
                {
                    // Aprobada: el horario de salida que tiene hoy el empleado es el que se pidió
                    var e = libres[0];
                    var antes = e.HorarioSalida.Subtract(TimeSpan.FromHours(1)).ToString(@"hh\:mm");
                    const string motivo = "Cambio de turno acordado con el empleado.";
                    Agregar(TiposSolicitud.EdicionEmpleado, supervisor, new SolicitudEdicionEmpleado
                    {
                        Motivo = motivo,
                        Cambios = { new CambioSolicitud { Campo = CamposEmpleado.HoraSalida, Anterior = antes, Nuevo = e.HorarioSalida.ToString(@"hh\:mm") } },
                        NuevosDatos = DatosEmpleadoSolicitud.Desde(e)
                    }, hoy.AddDays(-4).AddHours(10).AddMinutes(_azar.Next(0, 50)), Roles.IdAdministrador,
                        "Solicitud de Edición de Empleado", TiposNotificacion.Info, $"modificar los datos del empleado {e.NombreCompleto}",
                        "Solicitud Edición Empleado", $"Solicitud de edición enviada para empleado {e.NombreCompleto}. Motivo: {motivo}", administrador);
                }
                if (libres.Count > 1)
                {
                    // Rechazada, con el motivo que verá el Supervisor
                    var e = libres[1];
                    const string motivo = "Ausencias reiteradas durante la semana.";
                    Agregar(TiposSolicitud.InactivacionEmpleado, supervisor, new SolicitudInactivacionEmpleado
                    {
                        EmpleadoId = e.Id, Identificacion = e.Identificacion, NombreCompleto = e.NombreCompleto, Motivo = motivo
                    }, hoy.AddDays(-3).AddHours(15).AddMinutes(_azar.Next(0, 50)), Roles.IdAdministrador,
                        "Solicitud de Inactivación de Empleado", TiposNotificacion.Warning, $"inactivar al empleado {e.NombreCompleto}",
                        "Solicitud Inactivar Empleado", $"Solicitud de inactivación para {e.NombreCompleto}. Motivo: {motivo}", administrador,
                        "Las ausencias tienen soporte de incapacidad médica; el empleado sigue activo.");
                }
                if (libres.Count > 2)
                {
                    // Pendiente: el Administrador puede aprobarla en vivo
                    var e = libres[2];
                    var correo = CorreoDe(e.NombreCompleto);
                    var nuevos = DatosEmpleadoSolicitud.Desde(e);
                    nuevos.Correo = correo;
                    const string motivo = "El empleado cambió su correo de contacto.";
                    Agregar(TiposSolicitud.EdicionEmpleado, supervisor, new SolicitudEdicionEmpleado
                    {
                        Motivo = motivo,
                        Cambios = { new CambioSolicitud { Campo = CamposEmpleado.Correo, Anterior = e.Correo ?? "-", Nuevo = correo } },
                        NuevosDatos = nuevos
                    }, hoy.AddDays(-1).AddHours(16).AddMinutes(_azar.Next(0, 50)), Roles.IdAdministrador,
                        "Solicitud de Edición de Empleado", TiposNotificacion.Info, $"modificar los datos del empleado {e.NombreCompleto}",
                        "Solicitud Edición Empleado", $"Solicitud de edición enviada para empleado {e.NombreCompleto}. Motivo: {motivo}");
                }
            }

            if (auxiliar != null)
            {
                var usadas = (await _context.Empleados.Select(e => e.Identificacion).ToListAsync()).ToHashSet();
                var nombres = NombresAspirantes.OrderBy(_ => _azar.Next()).ToList();

                DatosEmpleadoSolicitud Aspirante(string nombre)
                {
                    string identificacion;
                    do identificacion = _azar.NextInt64(1_000_000_000, 1_099_999_999).ToString();
                    while (!usadas.Add(identificacion));
                    return new DatosEmpleadoSolicitud
                    {
                        Identificacion = identificacion, NombreCompleto = nombre, Correo = CorreoDe(nombre),
                        HorarioEntrada = new TimeSpan(7, 0, 0), HorarioSalida = new TimeSpan(16, 0, 0), FechaIngreso = hoy
                    };
                }

                // Rechazada por el Supervisor (él gestiona los registros que envían los Auxiliares) y otra pendiente,
                // salvo que ya haya un registro esperando (al cargar la demostración más de una vez)
                Agregar(TiposSolicitud.RegistroEmpleado, auxiliar, Aspirante(nombres[0]), hoy.AddDays(-2).AddHours(9).AddMinutes(_azar.Next(0, 50)),
                    Roles.IdSupervisor, "Solicitud de Nuevo Empleado", TiposNotificacion.Info, $"el registro del empleado {nombres[0]}",
                    "Solicitud Registro Empleado", $"Auxiliar solicitó registro de empleado: {nombres[0]}", supervisor,
                    "La identificación no coincide con el documento; envíala de nuevo con los datos del contrato.");
                if (!await _context.Solicitudes.AnyAsync(s => s.TipoSolicitudId == TiposSolicitud.RegistroEmpleado && s.Estado == EstadosSolicitud.Pendiente))
                    Agregar(TiposSolicitud.RegistroEmpleado, auxiliar, Aspirante(nombres[1]), sim.Ahora.AddMinutes(-_azar.Next(30, 90)),
                    Roles.IdSupervisor, "Solicitud de Nuevo Empleado", TiposNotificacion.Info, $"el registro del empleado {nombres[1]}",
                    "Solicitud Registro Empleado", $"Auxiliar solicitó registro de empleado: {nombres[1]}");
            }

            return creadas;
        }

        private static string CorreoDe(string nombre)
        {
            var partes = nombre.ToLowerInvariant()
                .Replace('á', 'a').Replace('é', 'e').Replace('í', 'i').Replace('ó', 'o').Replace('ú', 'u').Replace('ñ', 'n')
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return $"{partes[0]}.{partes[^1]}@pickflow.com";
        }

        private static DateTime Maximo(DateTime a, DateTime b) => a > b ? a : b;
        private static DateTime Minimo(DateTime a, DateTime b) => a < b ? a : b;
    }
}
