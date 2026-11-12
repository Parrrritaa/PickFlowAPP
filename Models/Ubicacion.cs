using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PickingApp.Models
{
    public class Ubicacion
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int BodegaId { get; set; }

        [ForeignKey("BodegaId")]
        public virtual Bodega? Bodega { get; set; }

        // Se genera automáticamente a partir de Pasillo/Estante/Nivel (ver BodegasController): PASXX-ESTXX-NIVXX
        [StringLength(50)]
        public string CodigoUbicacion { get; set; } = string.Empty;

        [Required(ErrorMessage = "El pasillo es obligatorio")]
        [StringLength(20)]
        [RegularExpression(@"^\d{1,9}$", ErrorMessage = "El pasillo solo debe contener números (hasta 9 dígitos)")]
        public string Pasillo { get; set; } = string.Empty;

        [Required(ErrorMessage = "El estante es obligatorio")]
        [StringLength(20)]
        [RegularExpression(@"^\d{1,9}$", ErrorMessage = "El estante solo debe contener números (hasta 9 dígitos)")]
        public string Estante { get; set; } = string.Empty;

        [Required(ErrorMessage = "El nivel es obligatorio")]
        [StringLength(20)]
        [RegularExpression(@"^\d{1,9}$", ErrorMessage = "El nivel solo debe contener números (hasta 9 dígitos)")]
        public string Nivel { get; set; } = string.Empty;

        public bool Activa { get; set; } = true;

        // Cada ubicación almacena un único producto.
        public int? ProductoId { get; set; }

        [ForeignKey("ProductoId")]
        public virtual Producto? Producto { get; set; }

        public virtual ICollection<DetallePedido> DetallesPedido { get; set; } = new List<DetallePedido>();

        // Código a partir de pasillo, estante y nivel (solo números): PAS01-EST02-NIV03
        public static string GenerarCodigo(string pasillo, string estante, string nivel) =>
            $"PAS{int.Parse(pasillo):D2}-EST{int.Parse(estante):D2}-NIV{int.Parse(nivel):D2}";
    }
}
