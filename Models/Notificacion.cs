using System.ComponentModel.DataAnnotations;

namespace PickingApp.Models
{
    // Aviso interno que aparece en la campana. Cada notificación es de un solo usuario, que la marca leída por su
    // cuenta; un aviso a todo un rol se guarda como una copia por usuario (Services/Avisos.cs).
    public class Notificacion
    {
        [Key]
        public int Id { get; set; }

        public int UsuarioDestinoId { get; set; }

        public Usuario? UsuarioDestino { get; set; }

        [Required]
        [StringLength(150)]
        public string Titulo { get; set; } = string.Empty;

        [Required]
        [StringLength(500)]
        public string Mensaje { get; set; } = string.Empty;

        [Required]
        public string Tipo { get; set; } = TiposNotificacion.Info; // Valores en TiposNotificacion

        public bool Leida { get; set; } = false;

        public DateTime FechaCreacion { get; set; } = DateTime.Now;

        [StringLength(255)]
        public string? Enlace { get; set; }
    }
}
