using PickingApp.Models;

namespace PickingApp.Services
{
    // Datos de los reportes de análisis (novedades, solicitudes, productos y carga horaria). El rendimiento contra
    // el estándar va dentro del reporte de tiempos por empleado (ReporteTiemposEmpleadoItem).
    // Cada clase se arma con De(...) a partir de listas ya filtradas por periodo: ReportesController las trae de
    // MySQL y las pruebas las usan con datos en memoria. ReportService solo las dibuja.

    // --- Novedades ---

    public record FilaNovedades(string Grupo, int Total, int Atendidas, double? HorasPromedioAtencion)
    {
        public int SinAtender => Total - Atendidas;
    }

    public class DatosNovedades
    {
        public int Total { get; init; }
        public int Atendidas { get; init; }
        public int SinAtender => Total - Atendidas;
        public double? HorasPromedioAtencion { get; init; }
        public List<FilaNovedades> PorTipo { get; init; } = new();
        public List<FilaNovedades> PorBodega { get; init; } = new();
        public List<FilaNovedades> PorEmpleado { get; init; } = new();
        public List<NovedadPedido> SinAtenderDetalle { get; init; } = new();

        // Requiere cada novedad con su Pedido, y del pedido la Bodega y el Empleado
        public static DatosNovedades De(IEnumerable<NovedadPedido> novedades)
        {
            var lista = novedades.ToList();
            return new DatosNovedades
            {
                Total = lista.Count,
                Atendidas = lista.Count(n => n.Atendida),
                HorasPromedioAtencion = HorasAtencion(lista),
                PorTipo = Agrupar(lista, n => n.TipoNovedad),
                PorBodega = Agrupar(lista, n => n.Pedido?.Bodega?.Nombre ?? "Sin bodega"),
                PorEmpleado = Agrupar(lista, n => n.Pedido?.Empleado?.NombreCompleto ?? "Sin asignar"),
                SinAtenderDetalle = lista.Where(n => !n.Atendida).OrderBy(n => n.FechaRegistro).ToList()
            };
        }

        private static List<FilaNovedades> Agrupar(List<NovedadPedido> lista, Func<NovedadPedido, string> grupo) => lista
            .GroupBy(grupo)
            .Select(g => new FilaNovedades(g.Key, g.Count(), g.Count(n => n.Atendida), HorasAtencion(g)))
            .OrderByDescending(f => f.Total)
            .ThenBy(f => f.Grupo)
            .ToList();

        // Desde que se registró hasta que se atendió; solo las atendidas
        private static double? HorasAtencion(IEnumerable<NovedadPedido> novedades)
        {
            var horas = novedades.Where(n => n.Atendida && n.FechaAtencion.HasValue)
                                 .Select(n => (n.FechaAtencion!.Value - n.FechaRegistro).TotalHours)
                                 .ToList();
            return horas.Any() ? horas.Average() : null;
        }
    }

    // --- Solicitudes y aprobaciones ---

    public record FilaSolicitudes(string Grupo, int Total, int Aprobadas, int Rechazadas, double? HorasPromedioRespuesta)
    {
        public int Pendientes => Total - Aprobadas - Rechazadas;
        public double? PorcentajeAprobacion => Aprobadas + Rechazadas == 0 ? null : 100.0 * Aprobadas / (Aprobadas + Rechazadas);
    }

    public class DatosSolicitudes
    {
        public FilaSolicitudes Totales { get; init; } = new("Total", 0, 0, 0, null);
        public List<FilaSolicitudes> PorTipo { get; init; } = new();
        public List<FilaSolicitudes> PorRevisor { get; init; } = new();

        // Requiere cada solicitud con TipoSolicitud y UsuarioResponde
        public static DatosSolicitudes De(IEnumerable<Solicitud> solicitudes)
        {
            var lista = solicitudes.ToList();
            return new DatosSolicitudes
            {
                Totales = Fila("Total", lista),
                PorTipo = lista.GroupBy(s => s.NombreTipo).Select(g => Fila(g.Key, g)).OrderByDescending(f => f.Total).ToList(),
                // Solo las respondidas tienen revisor
                PorRevisor = lista.Where(s => s.UsuarioResponde != null)
                                  .GroupBy(s => s.UsuarioResponde!.NombreCompleto)
                                  .Select(g => Fila(g.Key, g))
                                  .OrderByDescending(f => f.Total)
                                  .ToList()
            };
        }

        private static FilaSolicitudes Fila(string grupo, IEnumerable<Solicitud> solicitudes)
        {
            var lista = solicitudes.ToList();
            var horas = lista.Where(s => s.FechaRespuesta.HasValue)
                             .Select(s => (s.FechaRespuesta!.Value - s.FechaSolicitud).TotalHours)
                             .ToList();
            return new FilaSolicitudes(grupo, lista.Count,
                lista.Count(s => s.Estado == EstadosSolicitud.Aprobada),
                lista.Count(s => s.Estado == EstadosSolicitud.Rechazada),
                horas.Any() ? horas.Average() : null);
        }
    }

    // --- Productos y ubicaciones más pedidos ---

    public record FilaProducto(string Codigo, string Nombre, int Unidades, int Pedidos);

    public record FilaUbicacion(string Codigo, string Bodega, string Producto, int Visitas, int Unidades);

    public class DatosProductosUbicaciones
    {
        public const int Top = 15;

        public int Unidades { get; init; }
        public int ProductosDistintos { get; init; }
        public int UbicacionesVisitadas { get; init; }
        public List<FilaProducto> Productos { get; init; } = new();
        public List<FilaUbicacion> Ubicaciones { get; init; } = new();

        // Requiere cada detalle con su Ubicacion y la Bodega de la ubicación. Visitas = pedidos que pasaron por ella.
        public static DatosProductosUbicaciones De(IEnumerable<DetallePedido> detalles)
        {
            var lista = detalles.ToList();
            return new DatosProductosUbicaciones
            {
                Unidades = lista.Sum(d => d.Cantidad),
                ProductosDistintos = lista.Select(d => d.CodigoProducto).Distinct().Count(),
                UbicacionesVisitadas = lista.Select(d => d.UbicacionId).Distinct().Count(),
                Productos = lista.GroupBy(d => d.CodigoProducto)
                    .Select(g => new FilaProducto(g.Key, g.First().NombreProducto, g.Sum(d => d.Cantidad), g.Select(d => d.PedidoId).Distinct().Count()))
                    .OrderByDescending(f => f.Unidades).ThenBy(f => f.Codigo)
                    .Take(Top).ToList(),
                Ubicaciones = lista.GroupBy(d => d.UbicacionId)
                    .Select(g => new FilaUbicacion(
                        g.First().Ubicacion?.CodigoUbicacion ?? "Sin código",
                        g.First().Ubicacion?.Bodega?.Nombre ?? "Sin bodega",
                        $"{g.First().CodigoProducto} · {g.First().NombreProducto}",
                        g.Select(d => d.PedidoId).Distinct().Count(),
                        g.Sum(d => d.Cantidad)))
                    .OrderByDescending(f => f.Visitas).ThenByDescending(f => f.Unidades)
                    .Take(Top).ToList()
            };
        }
    }

    // --- Carga por hora y por día ---

    public record FilaHora(int Hora, int Cargados, int Completados, int EmpleadosEnTurno);

    public record FilaDia(string Dia, int Cargados, int Completados, double? PromedioMinutos);

    public class DatosCargaHoraria
    {
        private static readonly string[] NombresDias = { "Domingo", "Lunes", "Martes", "Miércoles", "Jueves", "Viernes", "Sábado" };

        public int Cargados { get; init; }
        public int Completados { get; init; }
        public List<FilaHora> PorHora { get; init; } = new();
        public List<FilaDia> PorDia { get; init; } = new();

        // Cargados por la hora de carga; completados por la hora en que se terminaron. Empleados en turno: los activos
        // cuyo horario cubre esa hora (un turno nocturno como 22:00-06:00 cruza la medianoche).
        public static DatosCargaHoraria De(IEnumerable<Pedido> pedidos, IEnumerable<Empleado> empleados)
        {
            var lista = pedidos.ToList();
            var activos = empleados.Where(e => e.Activo).ToList();
            var completados = lista.Where(p => p.Estado == PedidoEstados.Completado && p.FechaFinalizacion.HasValue).ToList();

            var porHora = Enumerable.Range(0, 24)
                .Select(h => new FilaHora(h,
                    lista.Count(p => p.FechaCarga.Hour == h),
                    completados.Count(p => p.FechaFinalizacion!.Value.Hour == h),
                    activos.Count(e => EnTurno(e, h))))
                // Solo las horas con movimiento o con alguien en turno: las demás no dicen nada
                .Where(f => f.Cargados > 0 || f.Completados > 0 || f.EmpleadosEnTurno > 0)
                .ToList();

            // La semana empieza el lunes
            var porDia = Enumerable.Range(1, 7).Select(i => (DayOfWeek)(i % 7))
                .Select(d =>
                {
                    var tiempos = completados.Where(p => p.FechaFinalizacion!.Value.DayOfWeek == d && p.TiempoProcesamientoMinutos.HasValue)
                                             .Select(p => p.TiempoProcesamientoMinutos!.Value).ToList();
                    return new FilaDia(NombresDias[(int)d],
                        lista.Count(p => p.FechaCarga.DayOfWeek == d),
                        completados.Count(p => p.FechaFinalizacion!.Value.DayOfWeek == d),
                        tiempos.Any() ? tiempos.Average() : null);
                })
                .ToList();

            return new DatosCargaHoraria { Cargados = lista.Count, Completados = completados.Count, PorHora = porHora, PorDia = porDia };
        }

        public static bool EnTurno(Empleado empleado, int hora)
        {
            var entrada = empleado.HorarioEntrada.Hours;
            var salida = empleado.HorarioSalida.Hours;
            return entrada <= salida ? hora >= entrada && hora < salida : hora >= entrada || hora < salida;
        }
    }
}
