using Microsoft.EntityFrameworkCore;
using PickingApp.Data;
using PickingApp.Models;

namespace PickingApp.Services
{
    public class PickingEngine : IPickingEngine
    {
        private readonly ApplicationDbContext _context;

        public PickingEngine(ApplicationDbContext context)
        {
            _context = context;
        }

        // Tiempo estándar = Σ (unidades × minutos por unidad de la categoría del producto) + ubicaciones × minutos de recorrido.
        // Los productos sin categoría usan la predeterminada.
        public async Task<double> CalcularTiempoEstandarAsync(int pedidoId)
        {
            var tiempos = await CalcularTiemposEstandarAsync(new[] { pedidoId });
            return tiempos.GetValueOrDefault(pedidoId);
        }

        // Varios pedidos en una sola consulta (p. ej. el historial de un empleado)
        public async Task<Dictionary<int, double>> CalcularTiemposEstandarAsync(IReadOnlyCollection<int> pedidoIds)
        {
            if (pedidoIds.Count == 0) return new Dictionary<int, double>();

            var lineas = await _context.DetallesPedido
                .Where(d => pedidoIds.Contains(d.PedidoId))
                .Select(d => new
                {
                    d.PedidoId,
                    d.Cantidad,
                    d.UbicacionId,
                    MinutosPorUnidad = (double?)d.Ubicacion!.Producto!.Categoria!.MinutosPorUnidad
                })
                .ToListAsync();

            var minutosPredeterminados = await ObtenerMinutosPredeterminadosAsync();
            var minutosPorUbicacion = await ObtenerMinutosPorUbicacionAsync();

            return lineas
                .GroupBy(l => l.PedidoId)
                .ToDictionary(g => g.Key, g =>
                {
                    var tiempoUnidades = g.Sum(l => l.Cantidad * (l.MinutosPorUnidad ?? minutosPredeterminados));
                    var tiempoRecorrido = g.Select(l => l.UbicacionId).Distinct().Count() * minutosPorUbicacion;
                    return Math.Round(Math.Max(1.0, tiempoUnidades + tiempoRecorrido), 1);
                });
        }

        // Tiempo estimado para un empleado concreto: tiempo estándar × su factor de rendimiento
        public async Task<double> CalcularTiempoEstimadoAsync(int pedidoId, int empleadoId)
        {
            var rendimiento = await CalcularRendimientoAsync(empleadoId);
            return rendimiento.Estimar(await CalcularTiempoEstandarAsync(pedidoId));
        }

        public async Task<RendimientoEmpleado> CalcularRendimientoAsync(int empleadoId)
        {
            var rendimientos = await CalcularRendimientosAsync(new[] { empleadoId });
            return rendimientos[empleadoId];
        }

        public async Task<Dictionary<int, RendimientoEmpleado>> CalcularRendimientosAsync(IReadOnlyCollection<int> empleadoIds)
        {
            var minimoPedidos = await ObtenerMinimoPedidosRendimientoAsync();

            var reinicios = await _context.Empleados
                .Where(e => empleadoIds.Contains(e.Id))
                .ToDictionaryAsync(e => e.Id, e => e.FechaReinicioPromedio);

            var completados = (await _context.Pedidos
                .Where(p => p.EmpleadoId != null && empleadoIds.Contains(p.EmpleadoId.Value)
                    && p.Estado == "Completado" && p.TiempoProcesamientoMinutos != null)
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

        private async Task<int> ObtenerMinimoPedidosRendimientoAsync()
        {
            var cfg = await _context.ConfiguracionesSistema.FirstOrDefaultAsync(c => c.Clave == "MinPedidosRendimiento");
            return cfg != null && int.TryParse(cfg.Valor, out var valor) && valor >= 1 ? valor : 3;
        }

        public async Task<double> ObtenerMinutosPredeterminadosAsync()
        {
            var minutos = await _context.CategoriasProducto
                .Where(c => c.EsPredeterminada)
                .Select(c => (double?)c.MinutosPorUnidad)
                .FirstOrDefaultAsync();
            return minutos ?? 1.5;
        }

        public async Task<double> ObtenerMinutosPorUbicacionAsync()
        {
            var cfg = await _context.ConfiguracionesSistema.FirstOrDefaultAsync(c => c.Clave == "MinutosPorUbicacion");
            return cfg != null && Formato.TryParseDecimal(cfg.Valor, out var valor) && valor >= 0 ? valor : 1.0;
        }

        public async Task<AsignacionValidationResult> ValidarAsignacionAsync(int pedidoId, int empleadoId)
        {
            var result = new AsignacionValidationResult();

            var pedido = await _context.Pedidos
                .Include(p => p.Detalles)
                .FirstOrDefaultAsync(p => p.Id == pedidoId);

            if (pedido == null)
            {
                result.PuedeAsignar = false;
                result.MensajeError = "El pedido no existe.";
                return result;
            }

            if (pedido.Estado == "Cancelado")
            {
                result.PuedeAsignar = false;
                result.MensajeError = "El pedido se encuentra cancelado y no puede reactivarse (RN-03-CU31).";
                return result;
            }

            if (pedido.Estado == "Completado")
            {
                result.PuedeAsignar = false;
                result.MensajeError = "El pedido ya se encuentra completado y no puede asignarse ni reasignarse.";
                return result;
            }

            if (pedido.Estado == PedidoEstados.Dividido)
            {
                result.PuedeAsignar = false;
                result.MensajeError = "El pedido fue dividido en partes: asigne cada parte por separado.";
                return result;
            }

            var empleado = await _context.Empleados
                .Include(e => e.PedidosAsignados)
                .FirstOrDefaultAsync(e => e.Id == empleadoId);

            if (empleado == null)
            {
                result.PuedeAsignar = false;
                result.MensajeError = "El empleado seleccionado no existe.";
                return result;
            }

            // Regla 1: Empleado Activo
            if (!empleado.Activo)
            {
                result.PuedeAsignar = false;
                result.MensajeError = $"El empleado '{empleado.NombreCompleto}' se encuentra inactivo.";
                result.MotivoCorto = "Inactivo";
                return result;
            }

            // Regla 2: Un solo pedido simultáneo
            var tienePedidoActivo = await _context.Pedidos
                .AnyAsync(p => p.EmpleadoId == empleadoId && PedidoEstados.EnCurso.Contains(p.Estado) && p.Id != pedidoId);

            if (tienePedidoActivo || empleado.EstadoDisponibilidad == "EnPicking")
            {
                result.PuedeAsignar = false;
                result.MensajeError = $"El empleado '{empleado.NombreCompleto}' ya tiene un pedido asignado en curso (RN-01-CU26).";
                result.MotivoCorto = "Con pedido en curso";
                return result;
            }

            // Regla 3: Validar Jornada Laboral
            var horaActual = DateTime.Now.TimeOfDay;
            bool enHorario = true;

            if (empleado.HorarioEntrada < empleado.HorarioSalida)
            {
                // Turno diurno normal
                enHorario = horaActual >= empleado.HorarioEntrada && horaActual <= empleado.HorarioSalida;
            }
            else
            {
                // Turno nocturno que cruza medianoche
                enHorario = horaActual >= empleado.HorarioEntrada || horaActual <= empleado.HorarioSalida;
            }

            if (!enHorario)
            {
                result.PuedeAsignar = false;
                result.MensajeError = $"El empleado '{empleado.NombreCompleto}' se encuentra fuera de su jornada laboral ({empleado.HorarioEntrada:hh\\:mm} - {empleado.HorarioSalida:hh\\:mm}).";
                result.MotivoCorto = "Fuera de jornada";
                return result;
            }

            // Regla 4: Tiempo estimado vs Tiempo restante de jornada
            result.TiempoEstandarMinutos = await CalcularTiempoEstandarAsync(pedidoId);
            result.Rendimiento = await CalcularRendimientoAsync(empleadoId);
            var tiempoEstimado = result.Rendimiento.Estimar(result.TiempoEstandarMinutos);
            result.TiempoEstimadoMinutos = tiempoEstimado;

            TimeSpan tiempoRestanteJornada;
            if (empleado.HorarioSalida > horaActual)
            {
                tiempoRestanteJornada = empleado.HorarioSalida - horaActual;
            }
            else
            {
                tiempoRestanteJornada = (TimeSpan.FromHours(24) - horaActual) + empleado.HorarioSalida;
            }

            if (tiempoRestanteJornada.TotalMinutes < tiempoEstimado)
            {
                result.PuedeAsignar = false;
                result.MensajeError = $"El tiempo estimado para el pedido ({Formato.Duracion(tiempoEstimado)}) supera el tiempo restante de la jornada del empleado ({Formato.Duracion(tiempoRestanteJornada.TotalMinutes)}).";
                result.MotivoCorto = $"Jornada insuficiente ({Formato.Duracion(tiempoRestanteJornada.TotalMinutes)} restantes)";
                return result;
            }

            // Regla 5: Advertencia de Experiencia (< 6 meses y > 10 ubicaciones)
            var totalUbicacionesPedido = pedido.Detalles.Select(d => d.UbicacionId).Distinct().Count();
            if (empleado.TieneMenosDeSeisMeses && totalUbicacionesPedido > 10)
            {
                result.GeneraAdvertencia = true;
                result.MensajeAdvertencia = $"ADVERTENCIA: El empleado '{empleado.NombreCompleto}' tiene menos de 6 meses de experiencia ({empleado.MesesExperiencia} meses) y el pedido contiene {totalUbicacionesPedido} ubicaciones (> 10). La asignación quedará registrada para auditoría del Administrador.";
            }

            result.PuedeAsignar = true;
            return result;
        }

        public double CalcularTiempoProcesamiento(DateTime fechaAsignacion, DateTime fechaFinalizacion, int margenMinutos = 2)
        {
            var inicioEfectivo = fechaAsignacion.AddMinutes(margenMinutos);
            if (fechaFinalizacion <= inicioEfectivo)
            {
                return 1.0; // Mínimo 1 minuto
            }

            var diferencia = (fechaFinalizacion - inicioEfectivo).TotalMinutes;
            return Math.Round(diferencia, 1);
        }
    }
}
