using System.ComponentModel.DataAnnotations;

namespace PickingApp.Models
{
    public class Producto
    {
        [Key]
        public int Id { get; set; }

        [Required(ErrorMessage = "El código de producto es obligatorio")]
        [StringLength(50)]
        public string Codigo { get; set; } = string.Empty;

        [Required(ErrorMessage = "El nombre de producto es obligatorio")]
        [StringLength(150)]
        public string Nombre { get; set; } = string.Empty;

        // Sin categoría = se usa la categoría predeterminada al calcular tiempos
        public int? CategoriaProductoId { get; set; }

        public virtual CategoriaProducto? Categoria { get; set; }

        // Unidades que trae la caja/empaque en que llega el producto. El tiempo de picking se calcula
        // por "toma" (caja o fracción de caja) en lugar de por unidad suelta cuando es mayor que 1.
        public int UnidadesPorCaja { get; set; } = 1;

        public const int MaxUnidadesPorCaja = 10000;

        public virtual ICollection<Ubicacion> Ubicaciones { get; set; } = new List<Ubicacion>();
    }
}
