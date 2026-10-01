using System.ComponentModel.DataAnnotations;

namespace PickingApp.Models
{
    // Catálogo de tipos de solicitud: Solicitud.TipoSolicitudId apunta aquí.
    // Los Id fijos están en TiposSolicitud (SolicitudDatos.cs) y se siembran en ApplicationDbContext.
    public class TipoSolicitud
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [StringLength(50)]
        public string Nombre { get; set; } = string.Empty;
    }
}
