using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using PickingApp.Data;
using PickingApp.Models;
using PickingApp.Models.ViewModels;
using PickingApp.Services;

namespace PickingApp.Controllers
{
    // Inicio y cierre de sesión, recuperación y cambio de contraseña
    public class AccountController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IAuditService _auditService;
        private readonly IEmailService _emailService;
        private readonly IAntiforgery _antiforgery;

        // Inyección de dependencias: ASP.NET entrega estos objetos según lo registrado en Program.cs
        public AccountController(ApplicationDbContext context, IAuditService auditService, IEmailService emailService, IAntiforgery antiforgery)
        {
            _context = context;
            _auditService = auditService;
            _emailService = emailService;
            _antiforgery = antiforgery;
        }

        // Fallos permitidos contra un mismo código antes de anularlo.
        private const int MaxIntentosCodigo = 5;

        // Asigna al usuario un código numérico de 6 dígitos, no predecible, válido 10 minutos.
        private static string AsignarCodigoVerificacion(Usuario usuario)
        {
            var codigo = System.Security.Cryptography.RandomNumberGenerator.GetInt32(0, 1000000).ToString("D6");
            usuario.CodigoVerificacion = codigo;
            usuario.CodigoVerificacionExpira = DateTime.Now.AddMinutes(10);
            usuario.IntentosCodigo = 0;
            return codigo;
        }

        private static bool CodigoCoincide(Usuario usuario, string codigo)
        {
            return !string.IsNullOrEmpty(usuario.CodigoVerificacion) &&
                usuario.CodigoVerificacion == codigo.Trim() &&
                usuario.CodigoVerificacionExpira.HasValue &&
                usuario.CodigoVerificacionExpira.Value >= DateTime.Now;
        }

        // Cuenta un fallo contra el código vigente y lo anula al llegar al límite, para que los
        // 6 dígitos no se puedan adivinar por fuerza bruta. Devuelve true si el código quedó anulado.
        private async Task<bool> RegistrarFalloCodigoAsync(Usuario usuario)
        {
            if (string.IsNullOrEmpty(usuario.CodigoVerificacion))
            {
                return false;
            }

            usuario.IntentosCodigo++;
            var anulado = usuario.IntentosCodigo >= MaxIntentosCodigo;
            if (anulado)
            {
                usuario.CodigoVerificacion = null;
                usuario.CodigoVerificacionExpira = null;
                usuario.PasswordPendienteHash = null;
                usuario.IntentosCodigo = 0;
            }
            await _context.SaveChangesAsync();

            await _auditService.LogAsync(usuario.Correo, usuario.NombreRol,
                anulado ? "Código Anulado" : "Código Incorrecto", "Seguridad",
                anulado
                    ? $"Código de verificación anulado tras {MaxIntentosCodigo} intentos fallidos."
                    : $"Código de verificación incorrecto (intento #{usuario.IntentosCodigo}).");

            return anulado;
        }

        // GET: muestra el formulario de inicio de sesión
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

        // POST: recibe el correo y la contraseña del formulario (LoginViewModel).
        // El token antifalsificación, que rechaza envíos que no vengan del formulario de la propia aplicación, se
        // valida aquí a mano y no con [ValidateAntiForgeryToken]: ese atributo responde con un 400 en blanco cuando
        // el formulario quedó viejo, y en el inicio de sesión eso tiene salida (ver abajo).
        [HttpPost]
        public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;

            if (!await _antiforgery.IsRequestValidAsync(HttpContext))
            {
                // El token se generó sin sesión, así que deja de valer en cuanto la sesión existe: pasa si el primer
                // envío sí entró pero el navegador lo cortó y reenvió el formulario. Ya hay sesión: se sigue al destino.
                if (User.Identity?.IsAuthenticated == true)
                {
                    return IrADestino(returnUrl);
                }

                // Sin sesión no se revisan las credenciales; ErrorLogin entrega un token nuevo para reintentar
                ModelState.AddModelError(string.Empty, "El formulario caducó. Escribe tu contraseña de nuevo e inténtalo otra vez.");
                return ErrorLogin(model);
            }

            // ModelState.IsValid es false si falla alguna validación del ViewModel ([Required], [EmailAddress])
            if (!ModelState.IsValid)
            {
                return ErrorLogin(model);
            }

            // SELECT del usuario por correo (los correos se guardan en minúsculas)
            var usuario = await _context.Usuarios
                .FirstOrDefaultAsync(u => u.Correo == model.Correo.Trim().ToLower());

            if (usuario == null)
            {
                // Mismo mensaje que con contraseña incorrecta: no revela qué correos existen
                ModelState.AddModelError(string.Empty, "Credenciales de acceso inválidas.");
                await _auditService.LogAsync(model.Correo, "Desconocido", "Fallo de Autenticación", "Autenticación", 
                    $"Intento fallido con correo no registrado: {model.Correo}");
                return ErrorLogin(model);
            }

            var maxIntentos = await ParametrosSistema.ObtenerAsync(_context, ParametrosSistema.MaxIntentosFallidos);

            if (usuario.Estado == EstadosUsuario.Bloqueado)
            {
                // RN-06-CU01: el bloqueo se levanta automáticamente a las 24 horas.
                if (usuario.FechaBloqueo.HasValue && DateTime.Now - usuario.FechaBloqueo.Value >= TimeSpan.FromHours(24))
                {
                    usuario.Estado = EstadosUsuario.Activo;
                    usuario.IntentosFallidos = 0;
                    usuario.FechaBloqueo = null;
                    await _context.SaveChangesAsync();

                    await _auditService.LogAsync(usuario.Correo, usuario.NombreRol, "Desbloqueo Automático", "Seguridad",
                        "Cuenta desbloqueada automáticamente tras 24 horas de bloqueo.");
                }
                else
                {
                    ModelState.AddModelError(string.Empty,
                        "Tu cuenta se encuentra bloqueada por superar el límite de intentos consecutivos fallidos. Contacta a un Administrador o espera 24 horas.");
                    await _auditService.LogAsync(usuario.Correo, usuario.NombreRol, "Acceso Bloqueado", "Autenticación",
                        "Intento de acceso a cuenta bloqueada");
                    return ErrorLogin(model);
                }
            }

            if (usuario.Estado == EstadosUsuario.Inactivo)
            {
                ModelState.AddModelError(string.Empty, "Tu cuenta se encuentra inactiva. Contacta al Administrador.");
                await _auditService.LogAsync(usuario.Correo, usuario.NombreRol, "Acceso Inactivo", "Autenticación", 
                    "Intento de acceso a cuenta inactiva");
                return ErrorLogin(model);
            }

            // Validar hash de contraseña: BCrypt aplica el mismo proceso a la contraseña escrita, con la sal
            // guardada dentro del hash, y compara el resultado. La contraseña nunca se desencripta.
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

                // RNF-06: Bloqueo al alcanzar los intentos consecutivos configurados, excepto Administrador
                if (usuario.IntentosFallidos >= maxIntentos && usuario.NombreRol != Roles.Administrador)
                {
                    usuario.Estado = EstadosUsuario.Bloqueado;
                    usuario.FechaBloqueo = DateTime.Now;

                    _context.Notificaciones.Add(new Notificacion
                    {
                        RolDestinoId = Roles.IdAdministrador,
                        Titulo = "Cuenta Bloqueada",
                        Mensaje = $"La cuenta del {ClaimsPrincipalExtensions.DescribirUsuario(usuario.NombreCompleto, usuario.NombreRol)} ha sido bloqueada por {usuario.IntentosFallidos} intentos fallidos.",
                        Tipo = TiposNotificacion.Danger,
                        FechaCreacion = DateTime.Now
                    });

                    await _context.SaveChangesAsync();

                    await _auditService.LogAsync(usuario.Correo, usuario.NombreRol, "Bloqueo de Cuenta", "Seguridad", 
                        $"Cuenta bloqueada tras {usuario.IntentosFallidos} intentos fallidos consecutivos");

                    ModelState.AddModelError(string.Empty,
                        $"Has alcanzado el límite de {maxIntentos} intentos fallidos. Tu cuenta ha sido bloqueada temporalmente por seguridad.");
                    return ErrorLogin(model);
                }

                await _context.SaveChangesAsync();

                var intentosRestantes = Math.Max(0, maxIntentos - usuario.IntentosFallidos);
                var mensajeError = usuario.NombreRol == Roles.Administrador
                    ? "Credenciales de acceso inválidas."
                    : $"Credenciales inválidas. Intentos restantes antes del bloqueo: {intentosRestantes}";

                ModelState.AddModelError(string.Empty, mensajeError);

                await _auditService.LogAsync(usuario.Correo, usuario.NombreRol, "Contraseña Incorrecta", "Autenticación", 
                    $"Intento fallido #{usuario.IntentosFallidos}");

                return ErrorLogin(model);
            }

            // Inicio de sesión exitoso: se reinician los intentos y se guarda la fecha de acceso
            usuario.IntentosFallidos = 0;
            usuario.UltimoAcceso = DateTime.Now;
            await _context.SaveChangesAsync();

            // Los claims son los datos del usuario que viajan dentro de la cookie de sesión.
            // Con ellos se lee después User.Identity.Name, el rol para [Authorize] y el Id del usuario.
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, usuario.Id.ToString()),
                new Claim(ClaimTypes.Name, usuario.NombreCompleto),
                new Claim(ClaimTypes.Email, usuario.Correo),
                new Claim(ClaimTypes.Role, usuario.NombreRol)
            };

            var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            // RNF-06: la expiración deslizante conserva esta duración en cada renovación, así que
            // define el cierre por inactividad de toda la sesión
            var minutosInactividad = await ParametrosSistema.ObtenerAsync(_context, ParametrosSistema.MinutosInactividadSesion);
            var authProperties = new AuthenticationProperties
            {
                IsPersistent = model.RememberMe,
                ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(minutosInactividad)
            };

            // Crea la cookie cifrada de sesión y la envía al navegador
            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(claimsIdentity),
                authProperties);

            await _auditService.LogAsync(usuario.Correo, usuario.NombreRol, "Inicio de Sesión", "Autenticación",
                "Inicio de sesión correcto en el sistema");

            // Marca la llegada como "recién logueado" para la animación de entrada del panel (solo se ve una vez).
            TempData["JustLoggedIn"] = true;

            return IrADestino(returnUrl);
        }

        private IActionResult IrADestino(string? returnUrl)
        {
            // Url.IsLocalUrl evita redirigir a otro sitio si alguien manipula el returnUrl
            var destino = !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)
                ? returnUrl
                : Url.Action("Index", "Home")!;

            // Con fetch, la página misma navega al destino (la cookie de sesión ya viene en esta respuesta)
            return EsEnvioConFetch ? Json(new { ok = true, url = destino }) : Redirect(destino);
        }

        // El formulario de inicio de sesión se envía con fetch (Views/Account/Login.cshtml) para mostrar el error
        // sin recargar la página. Sin JavaScript llega como un envío normal y se responde con la vista completa.
        private bool EsEnvioConFetch => Request.Headers["X-Requested-With"] == "fetch";

        // Errores del ModelState por campo ("" = mensaje general, "Correo", "Password").
        // Con fetch la página no se recarga, así que también viaja un token antifalsificación vigente: si el que
        // tenía el formulario caducó, el siguiente intento ya no falla. Sin fetch, la vista genera uno nuevo.
        private IActionResult ErrorLogin(LoginViewModel model)
        {
            if (!EsEnvioConFetch) return View(model);

            var errores = ModelState
                .Where(e => e.Value!.Errors.Count > 0)
                .ToDictionary(e => e.Key, e => e.Value!.Errors.Select(x => x.ErrorMessage).ToArray());
            var token = _antiforgery.GetAndStoreTokens(HttpContext).RequestToken;
            return Json(new { ok = false, errores, token });
        }

        // Cierra la sesión: borra la cookie de autenticación del navegador
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await _auditService.RegistrarAsync("Cierre de Sesión", "Autenticación", "Cierre de sesión de usuario");
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
        [EnableRateLimiting("CodigoVerificacion")]
        public async Task<IActionResult> SolicitarRecuperacion(SolicitarCodigoViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            // Los correos se guardan en minúsculas y sin espacios
            var correo = model.Correo.Trim().ToLower();
            var usuario = await _context.Usuarios
                .FirstOrDefaultAsync(u => u.Correo == correo);

            // El mensaje y el redireccionamiento son idénticos exista o no la cuenta, para no filtrar
            // qué correos están registrados (evita enumeración de usuarios).
            if (usuario != null)
            {
                var codigo = AsignarCodigoVerificacion(usuario);
                await _context.SaveChangesAsync();

                try
                {
                    await _emailService.EnviarCodigoVerificacionAsync(usuario.Correo, usuario.NombreCompleto, codigo);
                    await _auditService.LogAsync(usuario.Correo, usuario.NombreRol, "Solicitud Código Recuperación", "Seguridad",
                        "Se generó y envió un código de verificación para recuperar la contraseña.");
                }
                catch (Exception ex)
                {
                    await _auditService.LogAsync(usuario.Correo, usuario.NombreRol, "Error Envío Código", "Seguridad",
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
        [EnableRateLimiting("CodigoVerificacion")]
        public async Task<IActionResult> VerificarCodigoRecuperacion(VerificarCodigoRecuperacionViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var usuario = await _context.Usuarios
                .FirstOrDefaultAsync(u => u.Correo == model.Correo.Trim().ToLower());

            if (usuario == null || !CodigoCoincide(usuario, model.Codigo))
            {
                var anulado = usuario != null && await RegistrarFalloCodigoAsync(usuario);

                ModelState.AddModelError("Codigo", anulado
                    ? "Superaste el número de intentos permitidos. Solicita un código nuevo."
                    : "El código ingresado no es válido o ha expirado. Solicita uno nuevo.");
                return View(model);
            }

            // Se guarda el hash BCrypt de la nueva contraseña y se invalida el código usado
            usuario.PasswordHash = BCrypt.Net.BCrypt.HashPassword(model.NewPassword);
            usuario.CodigoVerificacion = null;
            usuario.CodigoVerificacionExpira = null;
            usuario.IntentosCodigo = 0;
            usuario.IntentosFallidos = 0;
            if (usuario.Estado == EstadosUsuario.Bloqueado)
            {
                usuario.Estado = EstadosUsuario.Activo;
                usuario.FechaBloqueo = null;
            }
            await _context.SaveChangesAsync();

            await _auditService.LogAsync(usuario.Correo, usuario.NombreRol, "Restablecimiento de Contraseña", "Seguridad",
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

            // El Id del usuario sale del claim guardado en la cookie al iniciar sesión
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out int userId))
            {
                return RedirectToAction("Login");
            }

            var usuario = await _context.Usuarios.FirstOrDefaultAsync(u => u.Id == userId);
            if (usuario == null)
            {
                return RedirectToAction("Login");
            }

            // Antes de cambiarla se comprueba que conoce la contraseña actual
            if (!BCrypt.Net.BCrypt.Verify(model.CurrentPassword, usuario.PasswordHash))
            {
                ModelState.AddModelError("CurrentPassword", "La contraseña actual no es correcta.");
                return View(model);
            }

            // Administrador: cambio directo e inmediato.
            if (usuario.NombreRol == Roles.Administrador)
            {
                usuario.PasswordHash = BCrypt.Net.BCrypt.HashPassword(model.NewPassword);
                await _context.SaveChangesAsync();

                await _auditService.LogAsync(usuario.Correo, usuario.NombreRol, "Cambio de Contraseña", "Seguridad",
                    "El usuario actualizó su contraseña exitosamente");

                TempData["SuccessMessage"] = "Tu contraseña ha sido actualizada con éxito.";
                return RedirectToAction("Index", "Home");
            }

            // CU-42: Supervisor y Auxiliar ya demostraron conocer su contraseña actual; ahora deben confirmar
            // un código de verificación enviado a su propio correo antes de aplicar el cambio (reemplaza la
            // aprobación manual del Administrador por una verificación de identidad más fuerte y automática).
            var codigo = AsignarCodigoVerificacion(usuario);
            usuario.PasswordPendienteHash = BCrypt.Net.BCrypt.HashPassword(model.NewPassword);
            await _context.SaveChangesAsync();

            try
            {
                await _emailService.EnviarCodigoVerificacionAsync(usuario.Correo, usuario.NombreCompleto, codigo);
                await _auditService.LogAsync(usuario.Correo, usuario.NombreRol, "Solicitud Código Cambio Contraseña", "Seguridad",
                    "Se envió un código de verificación al correo para confirmar el cambio de contraseña.");
            }
            catch (Exception ex)
            {
                await _auditService.LogAsync(usuario.Correo, usuario.NombreRol, "Error Envío Código", "Seguridad",
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
        [EnableRateLimiting("CodigoVerificacion")]
        public async Task<IActionResult> VerificarCodigoCambio(VerificarCodigoCambioViewModel model)
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out int userId))
            {
                return RedirectToAction("Login");
            }

            var usuario = await _context.Usuarios.FirstOrDefaultAsync(u => u.Id == userId);
            if (usuario == null)
            {
                return RedirectToAction("Login");
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            if (!CodigoCoincide(usuario, model.Codigo) || string.IsNullOrEmpty(usuario.PasswordPendienteHash))
            {
                var anulado = await RegistrarFalloCodigoAsync(usuario);

                ModelState.AddModelError("Codigo", anulado
                    ? "Superaste el número de intentos permitidos. Vuelve a solicitar el cambio de contraseña."
                    : "El código ingresado no es válido o ha expirado. Vuelve a solicitar el cambio de contraseña.");
                return View(model);
            }

            // Código correcto: el hash pendiente pasa a ser la contraseña vigente
            usuario.PasswordHash = usuario.PasswordPendienteHash;
            usuario.PasswordPendienteHash = null;
            usuario.CodigoVerificacion = null;
            usuario.CodigoVerificacionExpira = null;
            usuario.IntentosCodigo = 0;
            await _context.SaveChangesAsync();

            await _auditService.LogAsync(usuario.Correo, usuario.NombreRol, "Cambio de Contraseña", "Seguridad",
                "El usuario confirmó el cambio de contraseña mediante código enviado por correo.");

            TempData["SuccessMessage"] = "Tu contraseña ha sido actualizada con éxito.";
            return RedirectToAction("Index", "Home");
        }
    }
}
