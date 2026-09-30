using System.ComponentModel.DataAnnotations;

namespace PickingApp.Models
{
    // Cada clase de Models es una tabla de MySQL y cada propiedad una columna.
    // Los atributos validan los formularios y definen la columna: [Required] = NOT NULL,
    // [StringLength(100)] = VARCHAR(100), [Key] = llave primaria autoincremental.
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

        // Solo se guarda el hash BCrypt de la contraseña (se genera con BCrypt.HashPassword)
        [Required]
        public string PasswordHash { get; set; } = string.Empty;

        [Required]
        [StringLength(30)]
        public string Rol { get; set; } = Roles.Auxiliar; // Valores en Roles

        [Required]
        [StringLength(20)]
        public string Estado { get; set; } = EstadosUsuario.Activo; // Valores en EstadosUsuario

        // Se suma en cada login fallido; al llegar al máximo configurado la cuenta queda Bloqueada
        public int IntentosFallidos { get; set; } = 0;

        public DateTime? FechaBloqueo { get; set; }

        public DateTime FechaRegistro { get; set; } = DateTime.Now;

        public DateTime? UltimoAcceso { get; set; }

        // Código de verificación (6 dígitos) enviado por correo para recuperar u confirmar el cambio de contraseña.
        [StringLength(6)]
        public string? CodigoVerificacion { get; set; }

        public DateTime? CodigoVerificacionExpira { get; set; }

        // Fallos acumulados contra el código vigente; al llegar al límite el código se anula.
        public int IntentosCodigo { get; set; } = 0;

        // Hash de la nueva contraseña, pendiente de confirmar el código enviado al correo (CU-42).
        public string? PasswordPendienteHash { get; set; }
    }
}
