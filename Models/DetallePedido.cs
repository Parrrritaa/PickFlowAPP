using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PickingApp.Models
{
    // Una línea del pedido: qué producto, cuántas unidades y de qué ubicación se recoge
    public class DetallePedido
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int PedidoId { get; set; }

        [ForeignKey("PedidoId")]
        public virtual Pedido? Pedido { get; set; }

        [Required]
        [StringLength(50)]
        public string CodigoProducto { get; set; } = string.Empty;

        [Required]
        [StringLength(150)]
        public string NombreProducto { get; set; } = string.Empty;

        [Required]
        public int Cantidad { get; set; } = 1;

        [Required]
        public int UbicacionId { get; set; }

        [ForeignKey("UbicacionId")]
        public virtual Ubicacion? Ubicacion { get; set; }

        // La misma línea con otra cantidad, para repartirla entre las partes de un pedido dividido
        public DetallePedido Copia(int cantidad) => new()
        {
            CodigoProducto = CodigoProducto,
            NombreProducto = NombreProducto,
            Cantidad = cantidad,
            UbicacionId = UbicacionId
        };
    }
}
