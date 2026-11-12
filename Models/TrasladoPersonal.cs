using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PickingApp.Models
{
    // Cada cambio de bodega de un empleado o de un usuario: de aquí salen sus fechas en cada bodega
    // y quién sigue apareciendo como trasladado en la bodega que dejó.
    public class TrasladoPersonal
    {
        [Key]
        public int Id { get; set; }

        // Se llena uno de los dos, según quién se traslada
        public int? EmpleadoId { get; set; }

        [ForeignKey("EmpleadoId")]
        public virtual Empleado? Empleado { get; set; }

        public int? UsuarioId { get; set; }

        [ForeignKey("UsuarioId")]
        public virtual Usuario? Usuario { get; set; }

        public int BodegaOrigenId { get; set; }

        [ForeignKey("BodegaOrigenId")]
        public virtual Bodega? BodegaOrigen { get; set; }

        public int BodegaDestinoId { get; set; }

        [ForeignKey("BodegaDestinoId")]
        public virtual Bodega? BodegaDestino { get; set; }

        public DateTime Fecha { get; set; } = DateTime.Now;
    }
}
