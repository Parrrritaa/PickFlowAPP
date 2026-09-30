using System.ComponentModel.DataAnnotations;

namespace PickingApp.Models.ViewModels
{
    // Los ViewModels transportan lo que escribe el usuario en un formulario; no son tablas.
    // Sus atributos ([Required], [EmailAddress]...) generan los mensajes de validación de la vista.
    public class LoginViewModel
    {
        [Required(ErrorMessage = "El correo electrónico es obligatorio.")]
        [EmailAddress(ErrorMessage = "Ingrese un correo electrónico válido.")]
        public string Correo { get; set; } = string.Empty;

        [Required(ErrorMessage = "La contraseña es obligatoria.")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        public bool RememberMe { get; set; } = false;
    }

    // [Compare] valida que ConfirmPassword sea igual a NewPassword
    public class ChangePasswordViewModel
    {
        [Required(ErrorMessage = "La contraseña actual es obligatoria.")]
        [DataType(DataType.Password)]
        public string CurrentPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "La nueva contraseña es obligatoria.")]
        [StringLength(100, ErrorMessage = "La contraseña debe tener al menos {2} caracteres.", MinimumLength = 6)]
        [RegularExpression(@"^(?=.*[A-Za-z])(?=.*\d).+$", ErrorMessage = "La contraseña debe contener al menos una letra y un número.")]
        [DataType(DataType.Password)]
        public string NewPassword { get; set; } = string.Empty;

        [DataType(DataType.Password)]
        [Compare("NewPassword", ErrorMessage = "Las contraseñas no coinciden.")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    // CU-03: paso 1 de recuperación — se pide el correo para enviar el código de verificación.
    public class SolicitarCodigoViewModel
    {
        [Required(ErrorMessage = "El correo es obligatorio.")]
        [EmailAddress(ErrorMessage = "Ingrese un correo válido.")]
        public string Correo { get; set; } = string.Empty;
    }

    // CU-03: paso 2 de recuperación — código de verificación + nueva contraseña.
    public class VerificarCodigoRecuperacionViewModel
    {
        [Required]
        [EmailAddress]
        public string Correo { get; set; } = string.Empty;

        [Required(ErrorMessage = "El código de verificación es obligatorio.")]
        [StringLength(6, MinimumLength = 6, ErrorMessage = "El código debe tener 6 dígitos.")]
        public string Codigo { get; set; } = string.Empty;

        [Required(ErrorMessage = "La nueva contraseña es obligatoria.")]
        [StringLength(100, ErrorMessage = "La contraseña debe tener al menos {2} caracteres.", MinimumLength = 6)]
        [RegularExpression(@"^(?=.*[A-Za-z])(?=.*\d).+$", ErrorMessage = "La contraseña debe contener al menos una letra y un número.")]
        [DataType(DataType.Password)]
        public string NewPassword { get; set; } = string.Empty;

        [DataType(DataType.Password)]
        [Compare("NewPassword", ErrorMessage = "Las contraseñas no coinciden.")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    // CU-42: paso 2 del cambio de contraseña estando logueado — confirmación por código de verificación.
    public class VerificarCodigoCambioViewModel
    {
        [Required(ErrorMessage = "El código de verificación es obligatorio.")]
        [StringLength(6, MinimumLength = 6, ErrorMessage = "El código debe tener 6 dígitos.")]
        public string Codigo { get; set; } = string.Empty;
    }
}
