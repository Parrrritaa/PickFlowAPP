using PickingApp.Models;

namespace PickingApp.Services
{
    // El "reloj" de los datos de demostración (DatosDemostracion): recorre los días como si los empleados trabajaran
    // y deja cada pedido con sus fechas, tiempos y estado, junto con el LOG y las notificaciones que habría generado.
    public class SimulacionDemostracion
    {
        // Un empleado durante la simulación: qué tan rápido trabaja y a qué hora queda libre
        private sealed class Operario
        {
            public required Empleado Empleado { get; init; }
            public double Factor { get; init; }
            public DateTime Libre { get; set; }
            public bool Trabaja { get; set; }
        }

        private readonly Random _azar;
        private readonly List<Operario> _operarios;
        private readonly int _margen;
        private readonly HashSet<Pedido> _cancelarAntes = new();
        private Pedido? _cancelarEnProceso;
        private readonly Dictionary<Pedido, LogSistema> _logAsignacion = new();
        private readonly IReadOnlyList<Usuario> _usuarios;

        public DateTime Ahora { get; }
        public Usuario Administrador { get; }

        // Pedidos de la última carga de hoy: quedan pendientes para asignarlos en vivo
        public HashSet<Pedido> SinSimular { get; } = new();

        public List<LogSistema> Logs { get; } = new();
        public List<Notificacion> Notificaciones { get; } = new();

        // usuarios: quienes reciben los avisos dirigidos a un rol y, por bodega, quién firma el trabajo en el LOG
        public SimulacionDemostracion(DateTime ahora, Random azar, List<Empleado> empleados, int margenInicioMinutos,
            Usuario administrador, IReadOnlyList<Usuario> usuarios)
        {
            Ahora = ahora;
            _usuarios = usuarios;
            _azar = azar;
            _margen = margenInicioMinutos;
            Administrador = administrador;
            // Unos empleados son más rápidos que el estándar y otros más lentos: así el rendimiento de cada uno varía
            _operarios = empleados.Select(e => new Operario { Empleado = e, Factor = 0.8 + _azar.NextDouble() * 0.45 }).ToList();
        }

        // El prefijo distingue lo simulado de lo que hicieron de verdad los usuarios: el LOG sigue siendo evidencia
        private const string PrefijoLog = "[Demostración] ";

        public void Log(DateTime fecha, Usuario usuario, string accion, string detalle, string modulo = "Pedidos") =>
            Logs.Add(new LogSistema
            {
                FechaHora = fecha,
                Usuario = usuario.Correo,
                UsuarioId = usuario.Id,
                Rol = usuario.NombreRol,
                Accion = accion,
                Modulo = modulo,
                Detalle = PrefijoLog + detalle
            });

        // Quien trabaja los pedidos de una bodega: su Supervisor; si no tiene, el Administrador (que ve todas)
        public Usuario SupervisorDe(int? bodegaId) =>
            _usuarios.FirstOrDefault(u => u.RolId == Roles.IdSupervisor && u.BodegaId != null && u.BodegaId == bodegaId) ?? Administrador;

        // Un Excel con pedidos de varias bodegas solo lo puede cargar el Administrador
        public void LogCarga(DateTime fecha, List<Pedido> lote) =>
            Log(fecha, lote.Select(p => p.BodegaId).Distinct().Count() == 1 ? SupervisorDe(lote[0].BodegaId) : Administrador,
                AccionesLog.CargaExcelPedidos,
                $"Se procesó el archivo 'Pedidos_{fecha:yyyyMMdd_HHmm}.xlsx' registrando {lote.Count} pedidos ({lote.Sum(p => p.Detalles.Count)} líneas de productos).");

        // Igual que Avisos.AlRolAsync: una copia por cada usuario del rol; fuera del Administrador, solo los de esa bodega
        public void NotificarRol(DateTime fecha, int rolId, string titulo, string mensaje, string tipo, string enlace, bool? leida = null, int? bodegaId = null)
        {
            foreach (var usuario in _usuarios.Where(u => u.RolId == rolId && (rolId == Roles.IdAdministrador || (u.BodegaId != null && u.BodegaId == bodegaId))))
                NotificarUsuario(fecha, usuario.Id, titulo, mensaje, tipo, enlace, leida);
        }

        // Las notificaciones de días anteriores ya se leyeron; las de hoy quedan en la campana
        public void NotificarUsuario(DateTime fecha, int usuarioId, string titulo, string mensaje, string tipo, string enlace, bool? leida = null) =>
            Notificaciones.Add(new Notificacion
            {
                UsuarioDestinoId = usuarioId,
                Titulo = titulo,
                Mensaje = mensaje,
                Tipo = tipo,
                Enlace = enlace,
                FechaCreacion = fecha,
                Leida = leida ?? fecha.Date < Ahora.Date
            });

        // Recorre los días en orden. Cada día los empleados que trabajan toman pedidos por prioridad y hora de carga,
        // uno a la vez, dentro de su jornada; lo que no alcanza a hacerse pasa al día siguiente. Hoy se detiene en la
        // hora actual: lo que va por la mitad queda en curso y lo que no se ha tomado, pendiente.
        public void Ejecutar(List<Pedido> trabajo, Dictionary<int, double> estandar)
        {
            ElegirCancelaciones(trabajo);
            var arrastrados = new List<Pedido>();

            for (var dia = Ahora.Date.AddDays(-(DatosDemostracion.DiasSimulados - 1)); dia <= Ahora.Date; dia = dia.AddDays(1))
            {
                var delDia = trabajo.Where(p => p.FechaCarga.Date == dia && !SinSimular.Contains(p)).ToList();
                foreach (var p in delDia.Where(_cancelarAntes.Contains))
                    Cancelar(p, Minimo(p.FechaCarga.AddMinutes(_azar.Next(40, 200)), Ahora));

                var cola = arrastrados.Concat(delDia.Where(p => p.Estado == PedidoEstados.Pendiente))
                    .OrderBy(p => RangoPrioridad(p.Prioridad)).ThenBy(p => p.FechaCarga).ToList();

                foreach (var o in _operarios)
                {
                    // Nadie trabaja antes de su fecha de ingreso; los domingos trabaja más o menos la mitad
                    o.Trabaja = o.Empleado.FechaIngreso.Date <= dia && (dia.DayOfWeek != DayOfWeek.Sunday || _azar.NextDouble() < 0.5);
                    o.Libre = dia.Add(o.Empleado.HorarioEntrada);
                }

                foreach (var pedido in cola.ToList())
                {
                    TomarPedido(pedido, estandar[pedido.Id], dia);
                    if (pedido.Estado != PedidoEstados.Pendiente) cola.Remove(pedido);
                }
                arrastrados = cola;
            }
        }

        // Solo el personal de la bodega del pedido puede tomarlo (regla 0 de PickingEngine)
        private static bool DeSuBodega(Operario o, Pedido p) => o.Empleado.BodegaId != null && o.Empleado.BodegaId == p.BodegaId;

        // Lo toma el empleado de su bodega que quede libre primero y alcance a terminarlo en su jornada; si ninguno, sigue pendiente
        private void TomarPedido(Pedido pedido, double tiempoEstandar, DateTime dia)
        {
            foreach (var o in _operarios.Where(o => o.Trabaja && DeSuBodega(o, pedido)).OrderBy(o => o.Libre))
            {
                var asignacion = Maximo(o.Libre, pedido.FechaCarga.AddMinutes(_azar.Next(5, 20)));
                if (asignacion > Ahora) return; // todavía no ha pasado (hoy)

                var salida = Salida(o, dia);
                var inicio = asignacion.AddMinutes(_margen);
                var real = Real(tiempoEstandar, o);
                if (inicio.AddMinutes(real) > salida.AddMinutes(20)) continue; // no le alcanza la jornada: otro empleado

                Asignar(pedido, o, asignacion, tiempoEstandar);
                var fin = inicio.AddMinutes(real);
                var quien = o;

                // Cancelado a mitad del picking
                if (pedido == _cancelarEnProceso && inicio.AddMinutes(real * 0.4) <= Ahora)
                {
                    var momento = inicio.AddMinutes(real * 0.4);
                    Cancelar(pedido, momento);
                    o.Libre = momento.AddMinutes(_azar.Next(3, 10));
                    return;
                }

                // Reasignado a otro empleado que esté libre en ese momento
                var cambio = inicio.AddMinutes(real * (0.3 + _azar.NextDouble() * 0.25));
                var otro = _operarios.FirstOrDefault(x => x != o && x.Trabaja && DeSuBodega(x, pedido) && x.Libre <= cambio);
                if (otro != null && cambio <= Ahora && _azar.NextDouble() < 0.12)
                {
                    var avance = (cambio - inicio).TotalMinutes / real;
                    var inicio2 = cambio.AddMinutes(_margen);
                    var fin2 = inicio2.AddMinutes(Math.Max(3, Real(tiempoEstandar, otro) * (1 - avance)));
                    if (fin2 <= Salida(otro, dia).AddMinutes(20))
                    {
                        Reasignar(pedido, otro, cambio, tiempoEstandar, MotivosReasignacion());
                        o.Libre = cambio.AddMinutes(_azar.Next(3, 10));
                        quien = otro;
                        fin = fin2;
                    }
                }

                if (fin <= Ahora) Completar(pedido, fin);
                quien.Libre = fin.AddMinutes(_azar.Next(3, 12));
                return;
            }
        }

        // Lo que el azar pudo no producir se fuerza aquí: pedidos asignados y reasignados en curso, y un pedido
        // completado que pasó por una reasignación
        public void Garantizar(List<Pedido> trabajo, Dictionary<int, double> estandar)
        {
            var enCurso = trabajo.Where(p => p.EstaEnCurso).ToList();
            // Primero quienes están en su jornada a esta hora: son los que podrían tener un pedido en curso ahora
            var activos = _operarios.Where(o => o.Empleado.FechaIngreso.Date <= Ahora.Date)
                .OrderByDescending(o => o.Empleado.TrabajaA(Ahora.TimeOfDay)).ToList();
            List<Operario> Libres() => activos.Where(o => enCurso.All(p => p.EmpleadoId != o.Empleado.Id)).ToList();

            // Pendientes de hoy para tomar: primero los simulados; de la última carga siempre quedan al menos 2
            Pedido? Pendiente()
            {
                var p = trabajo.Where(x => x.Estado == PedidoEstados.Pendiente && !SinSimular.Contains(x)).OrderByDescending(x => x.FechaCarga).FirstOrDefault();
                if (p == null && SinSimular.Count(x => x.Estado == PedidoEstados.Pendiente) > 2)
                    p = SinSimular.First(x => x.Estado == PedidoEstados.Pendiente);
                return p;
            }

            if (!enCurso.Any(p => p.Estado == PedidoEstados.Reasignado))
            {
                var convertible = enCurso.FirstOrDefault(p => p.Estado == PedidoEstados.Asignado);
                var p = convertible ?? Pendiente();
                var destino = convertible != null ? activos.First(o => o.Empleado.Id == convertible.EmpleadoId)
                    : p == null ? null : Libres().FirstOrDefault(o => DeSuBodega(o, p));
                var anterior = destino == null ? null : activos.FirstOrDefault(o => o != destino && DeSuBodega(o, p!));
                if (p != null && destino != null && anterior != null)
                {
                    // El pedido estuvo antes con otro empleado; se reasignó hace un rato al que lo tiene.
                    // Las dos horas quedan entre la carga del pedido y la hora actual.
                    var asignado = Maximo(p.FechaCarga.AddMinutes(2), Ahora.AddMinutes(-_azar.Next(40, 60)));
                    var cambio = asignado.AddMinutes((Ahora - asignado).TotalMinutes * 0.6);
                    if (convertible != null) Logs.Remove(_logAsignacion[convertible]);
                    Asignar(p, anterior, asignado, estandar[p.Id]);
                    Reasignar(p, destino, cambio, estandar[p.Id], MotivosReasignacion());
                    if (!enCurso.Contains(p)) enCurso.Add(p);
                }
            }

            if (!enCurso.Any(p => p.Estado == PedidoEstados.Asignado))
            {
                var p = Pendiente();
                var destino = p == null ? null : Libres().FirstOrDefault(o => DeSuBodega(o, p));
                if (p != null && destino != null)
                {
                    Asignar(p, destino, Maximo(p.FechaCarga.AddMinutes(3), Ahora.AddMinutes(-_azar.Next(10, 25))), estandar[p.Id]);
                    enCurso.Add(p);
                }
            }

            // Un pedido completado con reasignación en su historial: la segunda mitad la hizo quien lo tiene
            if (!trabajo.Any(p => p.Estado == PedidoEstados.Completado && p.MotivoReasignacion != null))
            {
                var p = trabajo.FirstOrDefault(x => x.Estado == PedidoEstados.Completado && x.TiempoProcesamientoMinutos >= 20);
                var destino = p == null ? null : activos.First(o => o.Empleado.Id == p.EmpleadoId);
                var anterior = destino == null ? null : activos.FirstOrDefault(o => o != destino && DeSuBodega(o, p!));
                if (p != null && anterior != null)
                {
                    var fin = p.FechaFinalizacion!.Value;
                    var cambio = p.FechaInicioProceso!.Value.AddMinutes(p.TiempoProcesamientoMinutos!.Value * 0.4);
                    Logs.Remove(_logAsignacion[p]);
                    Logs.RemoveAll(l => l.Accion == AccionesLog.CompletarPedido && l.Detalle.StartsWith($"{PrefijoLog}Pedido {p.CodigoPedido} "));
                    Asignar(p, anterior, p.FechaAsignacion!.Value, p.TiempoEstandarMinutos!.Value);
                    Reasignar(p, destino!, cambio, p.TiempoEstandarMinutos!.Value, MotivosReasignacion());
                    Completar(p, fin);
                }
            }
        }

        // Novedades en algunos pedidos que pasaron por picking: las de días anteriores ya están atendidas
        // y una de hoy, sobre un pedido en curso, sigue abierta
        public List<NovedadPedido> CrearNovedades(List<Pedido> trabajo)
        {
            var novedades = new List<NovedadPedido>();
            var completados = trabajo.Where(p => p.Estado == PedidoEstados.Completado).OrderBy(_ => _azar.Next()).ToList();
            var elegidos = completados.Take(Math.Max(2, completados.Count * 15 / 100)).ToList();
            var abierto = trabajo.Where(p => p.EstaEnCurso).OrderBy(_ => _azar.Next()).FirstOrDefault();
            if (abierto != null) elegidos.Add(abierto);

            foreach (var p in elegidos)
            {
                var inicio = p.FechaInicioProceso!.Value;
                var hasta = p.FechaFinalizacion ?? Ahora;
                var fecha = inicio.AddMinutes((hasta - inicio).TotalMinutes * (0.2 + _azar.NextDouble() * 0.5));
                var (tipo, descripcion, atencion) = DatosDemostracion.Novedades[_azar.Next(DatosDemostracion.Novedades.Length)];
                var atendida = p != abierto;
                var atencionFecha = Minimo(fecha.AddMinutes(_azar.Next(15, 120)), Ahora);

                novedades.Add(new NovedadPedido
                {
                    Pedido = p,
                    TipoNovedad = tipo,
                    Descripcion = descripcion,
                    FechaRegistro = fecha,
                    RegistradoPor = SupervisorDe(p.BodegaId).NombreCompleto,
                    Atendida = atendida,
                    AtendidaPor = atendida ? Administrador.NombreCompleto : null,
                    FechaAtencion = atendida ? atencionFecha : null,
                    ObservacionesAtencion = atendida ? atencion : null
                });
                NotificarRol(fecha, Roles.IdAdministrador, "Novedad en Pedido", $"Se registró una novedad de tipo '{tipo}' en el pedido {p.CodigoPedido}.",
                    TiposNotificacion.Warning, $"/Pedidos/Detalle/{p.Id}", leida: atendida);
                Log(fecha, SupervisorDe(p.BodegaId), AccionesLog.RegistrarNovedad, $"Novedad registrada en pedido {p.CodigoPedido}: [{tipo}] {descripcion}");
            }
            return novedades;
        }

        // Después de guardar las novedades: el LOG de su atención lleva el número de cada una
        public void LogAtenciones(List<NovedadPedido> novedades)
        {
            foreach (var n in novedades.Where(n => n.Atendida))
                Log(n.FechaAtencion!.Value, Administrador, AccionesLog.AtenderNovedad, $"Novedad #{n.Id} del pedido {n.Pedido!.CodigoPedido} marcada como atendida.");
        }

        private void ElegirCancelaciones(List<Pedido> trabajo)
        {
            var anteriores = trabajo.Where(p => p.FechaCarga.Date < Ahora.Date && p.PedidoPadreId == null).ToList();
            foreach (var dia in anteriores.GroupBy(p => p.FechaCarga.Date))
                if (_azar.NextDouble() < 0.5) _cancelarAntes.Add(dia.ElementAt(_azar.Next(dia.Count())));
            if (_cancelarAntes.Count == 0 && anteriores.Count > 0) _cancelarAntes.Add(anteriores[_azar.Next(anteriores.Count)]);
            if (_azar.NextDouble() < 0.6)
                _cancelarEnProceso = anteriores.Where(p => !_cancelarAntes.Contains(p)).OrderBy(_ => _azar.Next()).FirstOrDefault();
        }

        private void Asignar(Pedido p, Operario o, DateTime momento, double tiempoEstandar)
        {
            var estimado = Estimado(tiempoEstandar, o);
            p.Asignar(o.Empleado.Id, momento.AddMinutes(_margen), estimado, tiempoEstandar);
            p.FechaAsignacion = momento;
            Log(momento, SupervisorDe(p.BodegaId), AccionesLog.AsignarPedido,
                $"Pedido {p.CodigoPedido} asignado al empleado {o.Empleado.NombreCompleto}. Estimado: {Formato.Duracion(estimado)} (estándar {Formato.Duracion(tiempoEstandar)} × rendimiento {o.Factor:0.00})");
            _logAsignacion[p] = Logs[^1];
        }

        private void Reasignar(Pedido p, Operario o, DateTime momento, double tiempoEstandar, string motivo)
        {
            var anterior = _operarios.First(x => x.Empleado.Id == p.EmpleadoId).Empleado.NombreCompleto;
            var estimado = Estimado(tiempoEstandar, o);
            p.Reasignar(o.Empleado.Id, motivo, momento.AddMinutes(_margen), estimado, tiempoEstandar);
            p.FechaAsignacion = momento;
            Log(momento, SupervisorDe(p.BodegaId), AccionesLog.ReasignarPedido,
                $"Pedido {p.CodigoPedido} reasignado de {anterior} a {o.Empleado.NombreCompleto}. Estimado: {Formato.Duracion(estimado)}. Motivo: {motivo}");
        }

        private void Completar(Pedido p, DateTime fin)
        {
            p.Estado = PedidoEstados.Completado;
            p.ConfirmacionFisica = true;
            p.FechaFinalizacion = fin;
            p.TiempoProcesamientoMinutos = Pedido.CalcularTiempoProcesamiento(p.FechaInicioProceso!.Value, fin);
            Log(fin, SupervisorDe(p.BodegaId), AccionesLog.CompletarPedido,
                $"Pedido {p.CodigoPedido} marcado como completado con entrega física. Tiempo real calculado: {p.TiempoProcesamientoMinutos} min");
        }

        private void Cancelar(Pedido p, DateTime momento)
        {
            var motivo = MotivosCancelacion();
            p.Estado = PedidoEstados.Cancelado;
            p.MotivoCancelacion = motivo;
            p.FechaFinalizacion = momento;
            Log(momento, SupervisorDe(p.BodegaId), AccionesLog.CancelarPedido, $"Pedido {p.CodigoPedido} cancelado. Motivo: {motivo}");
        }

        // Minutos reales: el estándar ajustado por el ritmo del empleado, con la variación de cada día
        private double Real(double tiempoEstandar, Operario o) => Math.Max(3, tiempoEstandar * o.Factor * (0.85 + _azar.NextDouble() * 0.35));

        private static double Estimado(double tiempoEstandar, Operario o) => Math.Round(Math.Max(1.0, tiempoEstandar * o.Factor), 1);

        // Jornada que cruza la medianoche: la salida es al día siguiente
        private static DateTime Salida(Operario o, DateTime dia) =>
            dia.Add(o.Empleado.HorarioSalida).AddDays(o.Empleado.HorarioSalida <= o.Empleado.HorarioEntrada ? 1 : 0);

        private string MotivosCancelacion() => DatosDemostracion.MotivosCancelacion[_azar.Next(DatosDemostracion.MotivosCancelacion.Length)];
        private string MotivosReasignacion() => DatosDemostracion.MotivosReasignacion[_azar.Next(DatosDemostracion.MotivosReasignacion.Length)];

        private static int RangoPrioridad(string prioridad) => Array.IndexOf(Prioridades.Todas, prioridad);
        private static DateTime Maximo(DateTime a, DateTime b) => a > b ? a : b;
        private static DateTime Minimo(DateTime a, DateTime b) => a < b ? a : b;
    }
}
