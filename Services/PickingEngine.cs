using Microsoft.EntityFrameworkCore;
using PickingApp.Data;
using PickingApp.Models;

namespace PickingApp.Services
{
    // Motor de cálculo del picking: tiempo estándar de un pedido, rendimiento de cada empleado
    // y reglas para decidir si un empleado puede recibir un pedido. Implementa IPickingEngine.
    public class PickingEngine : IPickingEngine
    {
        private readonly ApplicationDbContext _context;

        public PickingEngine(ApplicationDbContext context)
        {
            _context = context;
        }

        // Tiempo estándar = Σ (unidades × minutos por unidad de la categoría del producto ÷ unidades por caja del producto)
        // + ubicaciones × minutos de recorrido. Los productos sin categoría usan la predeterminada.
        //
        // UnidadesPorCaja modela que tomar una caja completa (o una fracción de ella) no cuesta tiempo lineal por
        // unidad: si un producto trae 12 por caja, recoger 12 unidades cuesta 1 "toma" (los mismos minutos que 1
        // unidad suelta), y recoger 25 cuesta 25/12 tomas. Productos con caja de 1 unidad (p. ej. un monitor) no
        // cambian de comportamiento.
        public async Task<double> CalcularTiempoEstandarAsync(int pedidoId)
        {
            var tiempos = await CalcularTiemposEstandarAsync(new[] { pedidoId });
            return tiempos.GetValueOrDefault(pedidoId);
        }

        // Varios pedidos en una sola consulta (p. ej. el historial de un empleado)
        public async Task<Dictionary<int, double>> CalcularTiemposEstandarAsync(IReadOnlyCollection<int> pedidoIds)
        {
            if (pedidoIds.Count == 0) return new Dictionary<int, double>();

            var lineas = await ObtenerLineasAsync(pedidoIds);
            var minutosPorUbicacion = await ParametrosSistema.ObtenerDecimalAsync(_context, ParametrosSistema.MinutosPorUbicacion);

            return lineas
                .GroupBy(l => l.PedidoId)
                .ToDictionary(g => g.Key, g =>
                {
                    // Tiempo de recoger: unidades × minutos por unidad (ya dividido por unidades por caja)
                    var tiempoUnidades = g.Sum(l => l.Cantidad * l.MinutosPorUnidad);
                    // Tiempo de caminar: cada ubicación distinta suma los minutos de recorrido configurados
                    var tiempoRecorrido = g.Select(l => l.UbicacionId).Distinct().Count() * minutosPorUbicacion;
                    // Nunca menos de 1 minuto, redondeado a 1 decimal
                    return Math.Round(Math.Max(1.0, tiempoUnidades + tiempoRecorrido), 1);
                });
        }

        // Minutos por unidad de cada línea del pedido, ya ajustados por caja. Los usa el reparto interactivo
        // de Pedidos/Dividir para no repetir la fórmula en la vista.
        public async Task<Dictionary<int, double>> CalcularMinutosPorLineaAsync(int pedidoId)
        {
            var lineas = await ObtenerLineasAsync(new[] { pedidoId });
            return lineas.ToDictionary(l => l.DetalleId, l => l.MinutosPorUnidad);
        }

        private sealed record LineaTiempo(int DetalleId, int PedidoId, int Cantidad, int UbicacionId, double MinutosPorUnidad);

        private async Task<List<LineaTiempo>> ObtenerLineasAsync(IReadOnlyCollection<int> pedidoIds)
        {
            var lineas = await _context.DetallesPedido
                .Where(d => pedidoIds.Contains(d.PedidoId))
                .Select(d => new
                {
                    d.Id,
                    d.PedidoId,
                    d.Cantidad,
                    d.UbicacionId,
                    MinutosCategoria = (double?)d.Ubicacion!.Producto!.Categoria!.MinutosPorUnidad,
                    UnidadesPorCaja = (int?)d.Ubicacion!.Producto!.UnidadesPorCaja
                })
                .ToListAsync();

            var minutosPredeterminados = await ObtenerMinutosPredeterminadosAsync();

            // Recoger una caja de N unidades cuesta lo mismo que 1 unidad suelta
            return lineas
                .Select(l => new LineaTiempo(l.Id, l.PedidoId, l.Cantidad, l.UbicacionId,
                    (l.MinutosCategoria ?? minutosPredeterminados) / Math.Max(1, l.UnidadesPorCaja ?? 1)))
                .ToList();
        }

        public async Task<Dictionary<int, RendimientoEmpleado>> CalcularRendimientosAsync(IReadOnlyCollection<int> empleadoIds)
        {
            var minimoPedidos = await ParametrosSistema.ObtenerAsync(_context, ParametrosSistema.MinPedidosRendimiento);

            var reinicios = await _context.Empleados
                .Where(e => empleadoIds.Contains(e.Id))
                .ToDictionaryAsync(e => e.Id, e => e.FechaReinicioPromedio);

            var completados = (await _context.Pedidos
                .Where(p => p.EmpleadoId != null && empleadoIds.Contains(p.EmpleadoId.Value)
                    && p.Estado == PedidoEstados.Completado && p.TiempoProcesamientoMinutos != null)
                .Select(p => new
                {
                    p.Id,
                    EmpleadoId = p.EmpleadoId!.Value,
                    p.FechaFinalizacion,
                    Real = p.TiempoProcesamientoMinutos!.Value,
                    p.TiempoEstandarMinutos
                })
                .ToListAsync())
                // Los pedidos anteriores a un reinicio de historial no cuentan
                .Where(p => reinicios.GetValueOrDefault(p.EmpleadoId) is not DateTime reinicio || p.FechaFinalizacion > reinicio)
                .ToList();

            // Pedidos completados antes de guardar el tiempo estándar: se calcula con las categorías actuales
            var estandarCalculado = await CalcularTiemposEstandarAsync(
                completados.Where(p => p.TiempoEstandarMinutos == null).Select(p => p.Id).ToList());

            var resultado = new Dictionary<int, RendimientoEmpleado>();
            foreach (var empleadoId in empleadoIds.Distinct())
            {
                var pedidos = completados
                    .Where(p => p.EmpleadoId == empleadoId)
                    .Select(p => new { p.Real, Estandar = p.TiempoEstandarMinutos ?? estandarCalculado.GetValueOrDefault(p.Id) })
                    .Where(p => p.Estandar > 0)
                    .ToList();

                var rendimiento = new RendimientoEmpleado { MinimoPedidos = minimoPedidos, PedidosConsiderados = pedidos.Count };
                if (pedidos.Any())
                {
                    // Ponderado por el tamaño del pedido: los pedidos grandes pesan más que los pequeños
                    rendimiento.FactorMedido = pedidos.Sum(p => p.Real) / pedidos.Sum(p => p.Estandar);
                }
                if (rendimiento.TieneHistorialSuficiente && rendimiento.FactorMedido.HasValue)
                {
                    // Se limita para que datos atípicos (pedidos cerrados de inmediato u olvidados abiertos) no distorsionen
                    rendimiento.Factor = Math.Round(Math.Clamp(rendimiento.FactorMedido.Value,
                        RendimientoEmpleado.FactorMinimo, RendimientoEmpleado.FactorMaximo), 2);
                }
                resultado[empleadoId] = rendimiento;
            }

            return resultado;
        }

        private async Task<double> ObtenerMinutosPredeterminadosAsync()
        {
            var minutos = await _context.CategoriasProducto
                .Where(c => c.EsPredeterminada)
                .Select(c => (double?)c.MinutosPorUnidad)
                .FirstOrDefaultAsync();
            return minutos ?? 1.5;
        }

        public async Task<AsignacionValidationResult> ValidarAsignacionAsync(int pedidoId, int empleadoId)
        {
            var validaciones = await ValidarAsignacionesAsync(pedidoId, new[] { empleadoId });
            return validaciones[empleadoId];
        }

        // Valida varios empleados contra el mismo pedido con un número fijo de consultas, sin importar
        // cuántos sean (la vista Asignar evalúa a todos los empleados activos).
        public async Task<Dictionary<int, AsignacionValidationResult>> ValidarAsignacionesAsync(int pedidoId, IReadOnlyCollection<int> empleadoIds)
        {
            var ids = empleadoIds.Distinct().ToList();

            var estadoPedido = await _context.Pedidos
                .Where(p => p.Id == pedidoId)
                .Select(p => p.Estado)
                .FirstOrDefaultAsync();

            var errorPedido = estadoPedido == null ? "El pedido no existe." : estadoPedido switch
            {
                PedidoEstados.Cancelado => "El pedido se encuentra cancelado y no puede reactivarse (RN-03-CU31).",
                PedidoEstados.Completado => "El pedido ya se encuentra completado y no puede asignarse ni reasignarse.",
                PedidoEstados.Dividido => "El pedido fue dividido en partes: asigne cada parte por separado.",
                _ => null
            };
            if (errorPedido != null)
            {
                return ids.ToDictionary(id => id, _ => new AsignacionValidationResult { MensajeError = errorPedido });
            }

            var empleados = await _context.Empleados
                .Where(e => ids.Contains(e.Id))
                .ToDictionaryAsync(e => e.Id);

            var ocupados = (await _context.Pedidos
                .Where(p => p.EmpleadoId != null && ids.Contains(p.EmpleadoId.Value)
                    && PedidoEstados.EnCurso.Contains(p.Estado) && p.Id != pedidoId)
                .Select(p => p.EmpleadoId!.Value)
                .ToListAsync())
                .ToHashSet();

            var contexto = new ContextoAsignacion
            {
                TiempoEstandarMinutos = await CalcularTiempoEstandarAsync(pedidoId),
                UbicacionesPedido = await _context.DetallesPedido
                    .Where(d => d.PedidoId == pedidoId)
                    .Select(d => d.UbicacionId)
                    .Distinct()
                    .CountAsync(),
                MesesMinimos = await ParametrosSistema.ObtenerAsync(_context, ParametrosSistema.MesesExperienciaAdvertencia),
                MaxUbicaciones = await ParametrosSistema.ObtenerAsync(_context, ParametrosSistema.MaxUbicacionesAdvertencia),
                HoraActual = DateTime.Now.TimeOfDay
            };
            var rendimientos = await CalcularRendimientosAsync(ids);

            return ids.ToDictionary(id => id, id => empleados.TryGetValue(id, out var empleado)
                ? EvaluarEmpleado(empleado, ocupados.Contains(id), rendimientos[id], contexto)
                : new AsignacionValidationResult { MensajeError = "El empleado seleccionado no existe." });
        }

        // Datos del pedido y de configuración comunes a todos los empleados evaluados
        private sealed class ContextoAsignacion
        {
            public double TiempoEstandarMinutos { get; init; }
            public int UbicacionesPedido { get; init; }
            public int MesesMinimos { get; init; }
            public int MaxUbicaciones { get; init; }
            public TimeSpan HoraActual { get; init; }
        }

        private static AsignacionValidationResult EvaluarEmpleado(Empleado empleado, bool tienePedidoEnCurso,
            RendimientoEmpleado rendimiento, ContextoAsignacion contexto)
        {
            var result = new AsignacionValidationResult
            {
                TiempoEstandarMinutos = contexto.TiempoEstandarMinutos,
                Rendimiento = rendimiento,
                TiempoEstimadoMinutos = rendimiento.Estimar(contexto.TiempoEstandarMinutos)
            };

            // Regla 1: Empleado Activo
            if (!empleado.Activo)
            {
                result.MensajeError = $"El empleado '{empleado.NombreCompleto}' se encuentra inactivo.";
                result.MotivoCorto = "Inactivo";
                return result;
            }

            // Regla 2: Un solo pedido simultáneo
            if (tienePedidoEnCurso)
            {
                result.MensajeError = $"El empleado '{empleado.NombreCompleto}' ya tiene un pedido asignado en curso (RN-01-CU26).";
                result.MotivoCorto = "Con pedido en curso";
                return result;
            }

            // Regla 3: Validar Jornada Laboral
            var horaActual = contexto.HoraActual;
            var enHorario = empleado.HorarioEntrada < empleado.HorarioSalida
                ? horaActual >= empleado.HorarioEntrada && horaActual <= empleado.HorarioSalida
                // Turno nocturno que cruza medianoche
                : horaActual >= empleado.HorarioEntrada || horaActual <= empleado.HorarioSalida;

            if (!enHorario)
            {
                result.MensajeError = $"El empleado '{empleado.NombreCompleto}' se encuentra fuera de su jornada laboral ({empleado.HorarioEntrada:hh\\:mm} - {empleado.HorarioSalida:hh\\:mm}).";
                result.MotivoCorto = "Fuera de jornada";
                return result;
            }

            // Regla 4: Tiempo estimado vs Tiempo restante de jornada.
            // Si la salida es menor que la hora actual, el turno termina después de medianoche.
            var tiempoRestanteJornada = empleado.HorarioSalida > horaActual
                ? empleado.HorarioSalida - horaActual
                : (TimeSpan.FromHours(24) - horaActual) + empleado.HorarioSalida;

            if (tiempoRestanteJornada.TotalMinutes < result.TiempoEstimadoMinutos)
            {
                result.MensajeError = $"El tiempo estimado para el pedido ({Formato.Duracion(result.TiempoEstimadoMinutos)}) supera el tiempo restante de la jornada del empleado ({Formato.Duracion(tiempoRestanteJornada.TotalMinutes)}).";
                result.MotivoCorto = $"Jornada insuficiente ({Formato.Duracion(tiempoRestanteJornada.TotalMinutes)} restantes)";
                return result;
            }

            // Regla 5: Advertencia de Experiencia (menos meses y más ubicaciones que los configurados)
            if (empleado.MesesExperiencia < contexto.MesesMinimos && contexto.UbicacionesPedido > contexto.MaxUbicaciones)
            {
                result.GeneraAdvertencia = true;
                result.MensajeAdvertencia = $"ADVERTENCIA: El empleado '{empleado.NombreCompleto}' tiene menos de {contexto.MesesMinimos} meses de experiencia ({empleado.MesesExperiencia} meses) y el pedido contiene {contexto.UbicacionesPedido} ubicaciones (> {contexto.MaxUbicaciones}). La asignación quedará registrada para auditoría del Administrador.";
                result.MotivoAdvertencia = $"menos de {contexto.MesesMinimos} meses de experiencia con más de {contexto.MaxUbicaciones} ubicaciones";
            }

            result.PuedeAsignar = true;
            return result;
        }

        // Tiempo real de picking: desde el inicio efectivo (asignación + margen) hasta la finalización
        public double CalcularTiempoProcesamiento(DateTime inicioProceso, DateTime fechaFinalizacion)
        {
            if (fechaFinalizacion <= inicioProceso)
            {
                return 1.0; // Mínimo 1 minuto
            }

            var diferencia = (fechaFinalizacion - inicioProceso).TotalMinutes;
            return Math.Round(diferencia, 1);
        }
    }
}
