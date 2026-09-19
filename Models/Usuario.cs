using System.ComponentModel.DataAnnotations;

namespace PickingApp.Models
{
    public class Usuario
    {
        [Key]
        public int Id { get; set; }

        [Required(ErrorMessage = "El nombre completo es obligatorio")]
        [StringLength(100)]
        public string NombreCompleto { get; set; } = string.Empty;

        // RN-02/03-CU03: se exige para verificar la identidad del solicitante en recuperación de contraseña.
        [StringLength(30)]
        [RegularExpression(@"^\d*$", ErrorMessage = "La identificación solo debe contener números")]
        public string? Identificacion { get; set; }

        [Required(ErrorMessage = "El correo es obligatorio")]
        [EmailAddress(ErrorMessage = "Formato de correo electrónico inválido")]
        [StringLength(100)]
        public string Correo { get; set; } = string.Empty;

        [Required]
        public string PasswordHash { get; set; } = string.Empty;

        [Required]
        [StringLength(30)]
        public string Rol { get; set; } = "Auxiliar"; // "Administrador", "Supervisor", "Auxiliar"

        [Required]
        [StringLength(20)]
        public string Estado { get; set; } = "Activo"; // "Activo", "Inactivo", "Bloqueado"

        public int IntentosFallidos { get; set; } = 0;

        public DateTime? FechaBloqueo { get; set; }

        public DateTime FechaRegistro { get; set; } = DateTime.Now;

        public DateTime? UltimoAcceso { get; set; }

        // RN-06-CU041: tras una recuperación aprobada, se obliga a establecer una nueva contraseña propia en el próximo inicio de sesión.
        public bool DebeCambiarContrasena { get; set; } = false;

        // Código de verificación (6 dígitos) enviado por correo para recuperar u confirmar el cambio de contraseña.
        [StringLength(6)]
        public string? CodigoVerificacion { get; set; }

        public DateTime? CodigoVerificacionExpira { get; set; }

        // Hash de la nueva contraseña, pendiente de confirmar el código enviado al correo (CU-42).
        public string? PasswordPendienteHash { get; set; }
    }
}
