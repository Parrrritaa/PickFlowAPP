using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickingApp.Data;
using PickingApp.Models;
using PickingApp.Models.ViewModels;
using PickingApp.Services;

namespace PickingApp.Controllers
{
    public class AccountController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IAuditService _auditService;
        private readonly IEmailService _emailService;

        public AccountController(ApplicationDbContext context, IAuditService auditService, IEmailService emailService)
        {
            _context = context;
            _auditService = auditService;
            _emailService = emailService;
        }

        // Código numérico de 6 dígitos, no predecible, para verificación por correo.
        private static string GenerarCodigoVerificacion()
        {
            return System.Security.Cryptography.RandomNumberGenerator.GetInt32(0, 1000000).ToString("D6");
        }

        [HttpGet]
        public IActionResult Login(string? returnUrl = null)
        {
            if (User.Identity != null && User.Identity.IsAuthenticated)
            {
                return RedirectToAction("Index", "Home");
            }

            ViewData["ReturnUrl"] = returnUrl;
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var usuario = await _context.Usuarios
                .FirstOrDefaultAsync(u => u.Correo.ToLower() == model.Correo.Trim().ToLower());

            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";

            if (usuario == null)
            {
                ModelState.AddModelError(string.Empty, "Credenciales de acceso inválidas.");
                await _auditService.LogAsync(model.Correo, "Desconocido", "Fallo de Autenticación", "Autenticación", 
                    $"Intento fallido con correo no registrado: {model.Correo}", ipAddress);
                return View(model);
            }

            if (usuario.Estado == "Bloqueado")
            {
                // RN-06-CU01: el bloqueo se levanta automáticamente a las 24 horas.
                if (usuario.FechaBloqueo.HasValue && DateTime.Now - usuario.FechaBloqueo.Value >= TimeSpan.FromHours(24))
                {
                    usuario.Estado = "Activo";
                    usuario.IntentosFallidos = 0;
                    usuario.FechaBloqueo = null;
                    await _context.SaveChangesAsync();

                    await _auditService.LogAsync(usuario.Correo, usuario.Rol, "Desbloqueo Automático", "Seguridad",
                        "Cuenta desbloqueada automáticamente tras 24 horas de bloqueo.", ipAddress);
                }
                else
                {
                    ModelState.AddModelError(string.Empty,
                        "Tu cuenta se encuentra bloqueada por superar los 5 intentos consecutivos fallidos. Contacta a un Administrador o espera 24 horas.");
                    await _auditService.LogAsync(usuario.Correo, usuario.Rol, "Acceso Bloqueado", "Autenticación",
                        "Intento de acceso a cuenta bloqueada", ipAddress);
                    return View(model);
                }
            }

            if (usuario.Estado == "Inactivo")
            {
                ModelState.AddModelError(string.Empty, "Tu cuenta se encuentra inactiva. Contacta al Administrador.");
                await _auditService.LogAsync(usuario.Correo, usuario.Rol, "Acceso Inactivo", "Autenticación", 
                    "Intento de acceso a cuenta inactiva", ipAddress);
                return View(model);
            }

            // Validar hash de contraseña
            bool passwordValida = false;
            try
            {
                passwordValida = BCrypt.Net.BCrypt.Verify(model.Password, usuario.PasswordHash);
            }
            catch
            {
                passwordValida = false;
            }

            if (!passwordValida)
            {
                usuario.IntentosFallidos++;

                // RNF-06: Bloqueo a los 5 intentos consecutivos excepto Administrador
                if (usuario.IntentosFallidos >= 5 && usuario.Rol != "Administrador")
                {
                    usuario.Estado = "Bloqueado";
                    usuario.FechaBloqueo = DateTime.Now;

                    _context.Notificaciones.Add(new Notificacion
                    {
                        RolDestino = "Administrador",
                        Titulo = "Cuenta Bloqueada",
                        Mensaje = $"La cuenta del usuario {usuario.NombreCompleto} ({usuario.Correo}) ha sido bloqueada por 5 intentos fallidos.",
                        Tipo = "Danger",
                        FechaCreacion = DateTime.Now
                    });

                    await _context.SaveChangesAsync();

                    await _auditService.LogAsync(usuario.Correo, usuario.Rol, "Bloqueo de Cuenta", "Seguridad", 
                        $"Cuenta bloqueada tras 5 intentos fallidos consecutivos desde IP {ipAddress}", ipAddress);

                    ModelState.AddModelError(string.Empty, 
                        "Has alcanzado el límite de 5 intentos fallidos. Tu cuenta ha sido bloqueada temporalmente por seguridad.");
                    return View(model);
                }

                await _context.SaveChangesAsync();

                var intentosRestantes = Math.Max(0, 5 - usuario.IntentosFallidos);
                var mensajeError = usuario.Rol == "Administrador"
                    ? "Credenciales de acceso inválidas."
                    : $"Credenciales inválidas. Intentos restantes antes del bloqueo: {intentosRestantes}";

                ModelState.AddModelError(string.Empty, mensajeError);

                await _auditService.LogAsync(usuario.Correo, usuario.Rol, "Contraseña Incorrecta", "Autenticación", 
                    $"Intento fallido #{usuario.IntentosFallidos}", ipAddress);

                return View(model);
            }

            // Inicio de sesión exitoso
            usuario.IntentosFallidos = 0;
            usuario.UltimoAcceso = DateTime.Now;
            await _context.SaveChangesAsync();

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, usuario.Id.ToString()),
                new Claim(ClaimTypes.Name, usuario.NombreCompleto),
                new Claim(ClaimTypes.Email, usuario.Correo),
                new Claim(ClaimTypes.Role, usuario.Rol)
            };

            var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var authProperties = new AuthenticationProperties
            {
                IsPersistent = model.RememberMe,
                ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(10) // 10 minutos de inactividad
            };

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(claimsIdentity),
                authProperties);

            await _auditService.LogAsync(usuario.Correo, usuario.Rol, "Inicio de Sesión", "Autenticación",
                "Inicio de sesión correcto en el sistema", ipAddress);

            // RN-06-CU041: si la contraseña fue asignada tras una recuperación aprobada, se obliga a establecer una nueva antes de continuar.
            if (usuario.DebeCambiarContrasena)
            {
                TempData["WarningMessage"] = "Tu contraseña fue restablecida. Debes establecer una nueva contraseña antes de continuar.";
                return RedirectToAction("CambiarContrasena");
            }

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }

            return RedirectToAction("Index", "Home");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            var userEmail = User.FindFirstValue(ClaimTypes.Email) ?? "Desconocido";
            var userRole = User.FindFirstValue(ClaimTypes.Role) ?? "Desconocido";

            await _auditService.LogAsync(userEmail, userRole, "Cierre de Sesión", "Autenticación", "Cierre de sesión de usuario");
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

            return RedirectToAction("Login", "Account");
        }

        [HttpGet]
        public IActionResult AccessDenied()
        {
            return View();
        }

        [HttpGet]
        public IActionResult SolicitarRecuperacion()
        {
            return View();
        }

        // CU-03: paso 1 — se envía un código de verificación de 6 dígitos al correo registrado.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SolicitarRecuperacion(SolicitarCodigoViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var correo = model.Correo.Trim();
            var usuario = await _context.Usuarios
                .FirstOrDefaultAsync(u => u.Correo.ToLower() == correo.ToLower());

            // El mensaje y el redireccionamiento son idénticos exista o no la cuenta, para no filtrar
            // qué correos están registrados (evita enumeración de usuarios).
            if (usuario != null)
            {
                var codigo = GenerarCodigoVerificacion();
                usuario.CodigoVerificacion = codigo;
                usuario.CodigoVerificacionExpira = DateTime.Now.AddMinutes(10);
                await _context.SaveChangesAsync();

                try
                {
                    await _emailService.EnviarCodigoVerificacionAsync(usuario.Correo, usuario.NombreCompleto, codigo);
                    await _auditService.LogAsync(usuario.Correo, usuario.Rol, "Solicitud Código Recuperación", "Seguridad",
                        "Se generó y envió un código de verificación para recuperar la contraseña.");
                }
                catch (Exception ex)
                {
                    await _auditService.LogAsync(usuario.Correo, usuario.Rol, "Error Envío Código", "Seguridad",
                        $"No se pudo enviar el código de verificación por correo: {ex.Message}");
                }
            }

            return RedirectToAction("VerificarCodigoRecuperacion", new { correo });
        }

        [HttpGet]
        public IActionResult VerificarCodigoRecuperacion(string correo)
        {
            if (string.IsNullOrWhiteSpace(correo))
            {
                return RedirectToAction("SolicitarRecuperacion");
            }

            return View(new VerificarCodigoRecuperacionViewModel { Correo = correo });
        }

        // CU-03: paso 2 — se valida el código y, si es correcto, el propio usuario establece su nueva contraseña.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> VerificarCodigoRecuperacion(VerificarCodigoRecuperacionViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var usuario = await _context.Usuarios
                .FirstOrDefaultAsync(u => u.Correo.ToLower() == model.Correo.Trim().ToLower());

            var codigoValido = usuario != null &&
                !string.IsNullOrEmpty(usuario.CodigoVerificacion) &&
                usuario.CodigoVerificacion == model.Codigo.Trim() &&
                usuario.CodigoVerificacionExpira.HasValue &&
                usuario.CodigoVerificacionExpira.Value >= DateTime.Now;

            if (!codigoValido)
            {
                ModelState.AddModelError("Codigo", "El código ingresado no es válido o ha expirado. Solicita uno nuevo.");
                return View(model);
            }

            usuario!.PasswordHash = BCrypt.Net.BCrypt.HashPassword(model.NewPassword);
            usuario.CodigoVerificacion = null;
            usuario.CodigoVerificacionExpira = null;
            usuario.DebeCambiarContrasena = false;
            usuario.IntentosFallidos = 0;
            if (usuario.Estado == "Bloqueado")
            {
                usuario.Estado = "Activo";
                usuario.FechaBloqueo = null;
            }
            await _context.SaveChangesAsync();

            await _auditService.LogAsync(usuario.Correo, usuario.Rol, "Restablecimiento de Contraseña", "Seguridad",
                "El usuario restableció su contraseña tras verificar el código enviado por correo.");

            TempData["SuccessMessage"] = "Tu contraseña ha sido restablecida con éxito. Ya puedes iniciar sesión.";
            return RedirectToAction("Login");
        }

        [Authorize]
        [HttpGet]
        public IActionResult CambiarContrasena()
        {
            return View();
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CambiarContrasena(ChangePasswordViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out int userId))
            {
                return RedirectToAction("Login");
            }

            var usuario = await _context.Usuarios.FindAsync(userId);
            if (usuario == null)
            {
                return RedirectToAction("Login");
            }

            if (!BCrypt.Net.BCrypt.Verify(model.CurrentPassword, usuario.PasswordHash))
            {
                ModelState.AddModelError("CurrentPassword", "La contraseña actual no es correcta.");
                return View(model);
            }

            // Administrador: cambio directo e inmediato.
            if (usuario.Rol == "Administrador")
            {
                usuario.PasswordHash = BCrypt.Net.BCrypt.HashPassword(model.NewPassword);
                usuario.DebeCambiarContrasena = false;
                await _context.SaveChangesAsync();

                await _auditService.LogAsync(usuario.Correo, usuario.Rol, "Cambio de Contraseña", "Seguridad",
                    "El usuario actualizó su contraseña exitosamente");

                TempData["SuccessMessage"] = "Tu contraseña ha sido actualizada con éxito.";
                return RedirectToAction("Index", "Home");
            }

            // Si esta contraseña fue forzada tras una recuperación aprobada, se aplica de inmediato:
            // el usuario ya demostró conocer la clave temporal, así que no tiene sentido pedir otra verificación.
            if (usuario.DebeCambiarContrasena)
            {
                usuario.PasswordHash = BCrypt.Net.BCrypt.HashPassword(model.NewPassword);
                usuario.DebeCambiarContrasena = false;
                await _context.SaveChangesAsync();

                await _auditService.LogAsync(usuario.Correo, usuario.Rol, "Cambio de Contraseña", "Seguridad",
                    "El usuario estableció su nueva contraseña tras una recuperación aprobada");

                TempData["SuccessMessage"] = "Tu contraseña ha sido actualizada con éxito.";
                return RedirectToAction("Index", "Home");
            }

            // CU-42: Supervisor y Auxiliar ya demostraron conocer su contraseña actual; ahora deben confirmar
            // un código de verificación enviado a su propio correo antes de aplicar el cambio (reemplaza la
            // aprobación manual del Administrador por una verificación de identidad más fuerte y automática).
            var codigo = GenerarCodigoVerificacion();
            usuario.CodigoVerificacion = codigo;
            usuario.CodigoVerificacionExpira = DateTime.Now.AddMinutes(10);
            usuario.PasswordPendienteHash = BCrypt.Net.BCrypt.HashPassword(model.NewPassword);
            await _context.SaveChangesAsync();

            try
            {
                await _emailService.EnviarCodigoVerificacionAsync(usuario.Correo, usuario.NombreCompleto, codigo);
                await _auditService.LogAsync(usuario.Correo, usuario.Rol, "Solicitud Código Cambio Contraseña", "Seguridad",
                    "Se envió un código de verificación al correo para confirmar el cambio de contraseña.");
            }
            catch (Exception ex)
            {
                await _auditService.LogAsync(usuario.Correo, usuario.Rol, "Error Envío Código", "Seguridad",
                    $"No se pudo enviar el código de verificación por correo: {ex.Message}");
            }

            TempData["WarningMessage"] = "Te enviamos un código de verificación a tu correo. Ingrésalo para confirmar el cambio de contraseña.";
            return RedirectToAction("VerificarCodigoCambio");
        }

        // CU-42: paso 2 — se confirma el código enviado al correo y se aplica la nueva contraseña pendiente.
        [Authorize]
        [HttpGet]
        public IActionResult VerificarCodigoCambio()
        {
            return View();
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> VerificarCodigoCambio(VerificarCodigoCambioViewModel model)
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out int userId))
            {
                return RedirectToAction("Login");
            }

            var usuario = await _context.Usuarios.FindAsync(userId);
            if (usuario == null)
            {
                return RedirectToAction("Login");
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var codigoValido = !string.IsNullOrEmpty(usuario.CodigoVerificacion) &&
                usuario.CodigoVerificacion == model.Codigo.Trim() &&
                usuario.CodigoVerificacionExpira.HasValue &&
                usuario.CodigoVerificacionExpira.Value >= DateTime.Now &&
                !string.IsNullOrEmpty(usuario.PasswordPendienteHash);

            if (!codigoValido)
            {
                ModelState.AddModelError("Codigo", "El código ingresado no es válido o ha expirado. Vuelve a solicitar el cambio de contraseña.");
                return View(model);
            }

            usuario.PasswordHash = usuario.PasswordPendienteHash!;
            usuario.PasswordPendienteHash = null;
            usuario.CodigoVerificacion = null;
            usuario.CodigoVerificacionExpira = null;
            usuario.DebeCambiarContrasena = false;
            await _context.SaveChangesAsync();

            await _auditService.LogAsync(usuario.Correo, usuario.Rol, "Cambio de Contraseña", "Seguridad",
                "El usuario confirmó el cambio de contraseña mediante código enviado por correo.");

            TempData["SuccessMessage"] = "Tu contraseña ha sido actualizada con éxito.";
            return RedirectToAction("Index", "Home");
        }
    }
}
