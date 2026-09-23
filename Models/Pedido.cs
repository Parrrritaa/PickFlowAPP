using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PickingApp.Models
{
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
        public string Estado { get; set; } = "Pendiente"; // "Pendiente", "Asignado", "EnProceso", "Completado", "Cancelado", "Reasignado", "Dividido"

        public int? EmpleadoId { get; set; }

        [ForeignKey("EmpleadoId")]
        public virtual Empleado? Empleado { get; set; }

        public DateTime FechaCarga { get; set; } = DateTime.Now;

        public DateTime? FechaAsignacion { get; set; }

        public DateTime? FechaInicioProceso { get; set; }

        public DateTime? FechaFinalizacion { get; set; }

        public double? TiempoEstimadoMinutos { get; set; }

        // Tiempo estándar según las categorías de sus productos, sin ajustar por empleado.
        // Se congela al asignar para que el historial no cambie si luego se editan las categorías.
        public double? TiempoEstandarMinutos { get; set; }

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

        public virtual ICollection<DetallePedido> Detalles { get; set; } = new List<DetallePedido>();
        public virtual ICollection<NovedadPedido> Novedades { get; set; } = new List<NovedadPedido>();
    }
}
