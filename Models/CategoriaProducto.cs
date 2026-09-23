using System.ComponentModel.DataAnnotations;

namespace PickingApp.Models
{
    // Categoría de manipulación: define cuánto tarda, en promedio, recoger una unidad de sus productos.
    // Permite que el tiempo estándar distinga un mouse de una torre de PC.
    public class CategoriaProducto
    {
        [Key]
        public int Id { get; set; }

        [Required(ErrorMessage = "El nombre de la categoría es obligatorio")]
        [StringLength(50)]
        public string Nombre { get; set; } = string.Empty;

        [StringLength(150)]
        public string? Descripcion { get; set; }

        // Minutos estándar para recoger una unidad (acepta decimales, p. ej. 0,3)
        public double MinutosPorUnidad { get; set; }

        // Se usa para los productos sin categoría asignada. Siempre hay exactamente una.
        public bool EsPredeterminada { get; set; }

        public virtual ICollection<Producto> Productos { get; set; } = new List<Producto>();
    }
}
