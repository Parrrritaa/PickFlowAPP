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

        public virtual ICollection<Ubicacion> Ubicaciones { get; set; } = new List<Ubicacion>();
    }
}
