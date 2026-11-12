using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PickingApp.Models
{
    // Registro de auditoría: cada acción importante guarda una fila aquí mediante AuditService
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

        // Cómo se muestra el usuario en el LOG: "Nombre (Rol)", nunca el correo.
        // Requiere cargar UsuarioAsociado. El rol es el que tenía al momento de la acción.
        [NotMapped]
        public string UsuarioParaMostrar
        {
            get
            {
                var rol = Rol is Roles.Administrador or Roles.Supervisor or Roles.Auxiliar ? Rol : UsuarioAsociado?.NombreRol;

                if (UsuarioAsociado != null)
                    return rol != null ? $"{UsuarioAsociado.NombreCompleto} ({rol})" : UsuarioAsociado.NombreCompleto;

                if (!Usuario.Contains('@')) return Usuario; // "Sistema"

                // Correo que ya no corresponde a ningún usuario registrado
                return rol != null ? $"Usuario no registrado ({rol})" : "Correo no registrado";
            }
        }
    }
}
