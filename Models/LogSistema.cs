using System.ComponentModel.DataAnnotations;

namespace PickingApp.Models
{
    public class LogSistema
    {
        [Key]
        public int Id { get; set; }

        public DateTime FechaHora { get; set; } = DateTime.Now;

        [Required]
        [StringLength(100)]
        public string Usuario { get; set; } = string.Empty;

        // FK al usuario registrado que realizó la acción; null si no corresponde a un usuario existente
        // (p. ej. intentos de acceso con correos no registrados o acciones del "Sistema").
        // Usuario y Rol se conservan como texto para mantener la trazabilidad histórica.
        public int? UsuarioId { get; set; }

        public Usuario? UsuarioAsociado { get; set; }

        [Required]
        [StringLength(50)]
        public string Rol { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        public string Accion { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        public string Modulo { get; set; } = string.Empty;

        [Required]
        public string Detalle { get; set; } = string.Empty;

        [StringLength(50)]
        public string? DireccionIp { get; set; }
    }
}
