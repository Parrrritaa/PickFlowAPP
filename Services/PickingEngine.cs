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
            var lineas = await _context.DetallesPedido
                .Where(d => d.PedidoId == pedidoId)
                .Select(d => new
                {
                    d.Cantidad,
                    d.UbicacionId,
                    MinutosPorUnidad = (double?)d.Ubicacion!.Producto!.Categoria!.MinutosPorUnidad
                })
                .ToListAsync();

            if (!lineas.Any()) return 0;

            var minutosPredeterminados = await ObtenerMinutosPredeterminadosAsync();
            var minutosPorUbicacion = await ObtenerMinutosPorUbicacionAsync();

            var tiempoUnidades = lineas.Sum(l => l.Cantidad * (l.MinutosPorUnidad ?? minutosPredeterminados));
            var tiempoRecorrido = lineas.Select(l => l.UbicacionId).Distinct().Count() * minutosPorUbicacion;

            return Math.Round(Math.Max(1.0, tiempoUnidades + tiempoRecorrido), 1);
        }

        // Tiempo estimado para un empleado concreto. Hoy coincide con el tiempo estándar del pedido.
        public async Task<double> CalcularTiempoEstimadoAsync(int pedidoId, int empleadoId)
        {
            return await CalcularTiempoEstandarAsync(pedidoId);
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
            var tiempoEstimado = await CalcularTiempoEstimadoAsync(pedidoId, empleadoId);
            result.TiempoEstimadoMinutos = tiempoEstimado;
            result.TiempoEstandarMinutos = await CalcularTiempoEstandarAsync(pedidoId);

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
