using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PickingApp.Models
{
    // Pedido de picking cargado desde Excel. Sus productos están en Detalles (tabla DetallesPedido)
    // y su estado sigue el ciclo Pendiente → Asignado/Reasignado → Completado o Cancelado.
    public class Pedido
    {
        [Key]
        public int Id { get; set; }

        [Required(ErrorMessage = "El código de pedido es obligatorio")]
        [StringLength(50)]
        public string CodigoPedido { get; set; } = string.Empty;

        [Required]
        public int BodegaId { get; set; }

        [ForeignKey("BodegaId")]
        public virtual Bodega? Bodega { get; set; }

        [Required]
        [StringLength(20)]
        public string Prioridad { get; set; } = "Media"; // "Alta", "Media", "Baja"

        [Required]
        [StringLength(30)]
        public string Estado { get; set; } = PedidoEstados.Pendiente; // Valores en PedidoEstados

        public int? EmpleadoId { get; set; }

        [ForeignKey("EmpleadoId")]
        public virtual Empleado? Empleado { get; set; }

        public DateTime FechaCarga { get; set; } = DateTime.Now;

        public DateTime? FechaAsignacion { get; set; }

        public DateTime? FechaInicioProceso { get; set; }

        public DateTime? FechaFinalizacion { get; set; }

        // Tiempo que calcula PickingEngine para el empleado asignado (estándar × factor de rendimiento)
        public double? TiempoEstimadoMinutos { get; set; }

        // Tiempo estándar según las categorías de sus productos, sin ajustar por empleado.
        // Se congela al asignar para que el historial no cambie si luego se editan las categorías.
        public double? TiempoEstandarMinutos { get; set; }

        // Tiempo real: de FechaInicioProceso a FechaFinalizacion, se calcula al completar el pedido
        public double? TiempoProcesamientoMinutos { get; set; }

        [StringLength(250)]
        public string? MotivoCancelacion { get; set; }

        [StringLength(250)]
        public string? MotivoReasignacion { get; set; }

        public bool ConfirmacionFisica { get; set; } = false;

        // División de pedidos grandes: el pedido original queda "Dividido" y cada parte es un pedido
        // normal (se asigna, completa o cancela por separado) con código PED-XXX-P1, PED-XXX-P2...
        public int? PedidoPadreId { get; set; }

        public virtual Pedido? PedidoPadre { get; set; }

        public int? NumeroParte { get; set; }

        public virtual ICollection<Pedido> Partes { get; set; } = new List<Pedido>();

        // Propiedades de navegación: EF las llena con .Include(p => p.Detalles) en las consultas
        public virtual ICollection<DetallePedido> Detalles { get; set; } = new List<DetallePedido>();
        public virtual ICollection<NovedadPedido> Novedades { get; set; } = new List<NovedadPedido>();

        // Reglas de estado: controladores y vistas preguntan aquí en vez de comparar cadenas por su cuenta.

        [NotMapped]
        public bool EstaEnCurso => PedidoEstados.EnCurso.Contains(Estado);

        [NotMapped]
        public bool EstaFinalizado => PedidoEstados.Finalizados.Contains(Estado);

        [NotMapped]
        public bool EstaDividido => Estado == PedidoEstados.Dividido;

        // Requiere Detalles cargados.
        public string? MotivoNoDivisible()
        {
            if (PedidoPadreId != null) return "Una parte no puede dividirse de nuevo.";
            if (Estado != PedidoEstados.Pendiente) return "Solo se pueden dividir pedidos pendientes (sin asignar).";
            if (Detalles.Sum(d => d.Cantidad) < 2) return "El pedido necesita al menos 2 unidades para dividirse.";
            return null;
        }

        public const int MaxPartes = 20;

        // Nunca más partes que unidades. Requiere Detalles cargados.
        [NotMapped]
        public int MaxPartesPermitidas => Math.Min(MaxPartes, Detalles.Sum(d => d.Cantidad));

        // Partes necesarias para que cada una quepa en una jornada a ritmo estándar. Requiere Detalles cargados.
        public int PartesSugeridas(double tiempoEstandar, double jornadaMinutos) =>
            Math.Clamp((int)Math.Ceiling(tiempoEstandar / jornadaMinutos), 2, Math.Max(2, MaxPartesPermitidas));

        // Las partes se vuelven a unir mientras ninguna se haya asignado ni tenga novedades.
        // Requiere Partes con sus Novedades cargadas.
        [NotMapped]
        public bool PuedeDeshacerDivision =>
            EstaDividido && Partes.All(pa => pa.Estado == PedidoEstados.Pendiente && !pa.Novedades.Any());

        public void Asignar(int empleadoId, DateTime inicioProceso, double tiempoEstimado, double tiempoEstandar) =>
            TomarEmpleado(PedidoEstados.Asignado, empleadoId, inicioProceso, tiempoEstimado, tiempoEstandar);

        public void Reasignar(int empleadoId, string motivo, DateTime inicioProceso, double tiempoEstimado, double tiempoEstandar)
        {
            TomarEmpleado(PedidoEstados.Reasignado, empleadoId, inicioProceso, tiempoEstimado, tiempoEstandar);
            MotivoReasignacion = motivo;
        }

        // Solo cambia el objeto en memoria; el controlador lo guarda en MySQL con SaveChangesAsync
        private void TomarEmpleado(string estado, int empleadoId, DateTime inicioProceso, double tiempoEstimado, double tiempoEstandar)
        {
            EmpleadoId = empleadoId;
            Estado = estado;
            FechaAsignacion = DateTime.Now;
            FechaInicioProceso = inicioProceso;
            TiempoEstimadoMinutos = tiempoEstimado;
            TiempoEstandarMinutos = tiempoEstandar;
        }

        // Cuando todas las partes terminan, el pedido original se cierra: completado si al menos una parte
        // se entregó, cancelado si no. Requiere Partes cargadas. Devuelve true si se cerró.
        public bool CerrarSiTerminaronLasPartes()
        {
            if (!EstaDividido || Partes.Any(pa => !pa.EstaFinalizado)) return false;

            var algunaCompletada = Partes.Any(pa => pa.Estado == PedidoEstados.Completado);
            Estado = algunaCompletada ? PedidoEstados.Completado : PedidoEstados.Cancelado;
            ConfirmacionFisica = algunaCompletada;
            FechaFinalizacion = Partes.Max(pa => pa.FechaFinalizacion) ?? DateTime.Now;
            if (!algunaCompletada) MotivoCancelacion = "Todas sus partes fueron canceladas.";
            return true;
        }
    }
}
