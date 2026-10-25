using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickingApp.Data;
using PickingApp.Models;
using PickingApp.Services;

namespace PickingApp.Controllers
{
    // Gestión de usuarios del sistema. [Authorize(Roles = ...)] en la clase aplica a todas sus acciones:
    // solo el Administrador entra; los demás roles reciben la página de acceso denegado.
    [Authorize(Roles = Roles.Administrador)]
    public class UsuariosController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IAuditService _auditService;

        public UsuariosController(ApplicationDbContext context, IAuditService auditService)
        {
            _context = context;
            _auditService = auditService;
        }

        public async Task<IActionResult> Index()
        {
            var usuarios = await _context.Usuarios
                .OrderBy(u => u.Rol!.Nombre)
                .ThenBy(u => u.NombreCompleto)
                .ToListAsync();

            return View(usuarios);
        }

        // Consulta del formulario de Crear (site.js, [data-pf-correo-disponible]) para avisar del correo repetido sin
        // enviar el formulario: si el servidor lo rechazara, la página se recargaría y el navegador vaciaría las contraseñas.
        // POST para que el correo no quede en la URL ni en los registros del servidor.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CorreoDisponible(string? correo)
        {
            var normalizado = ReglasEmpleado.NormalizarCorreo(correo);
            return Json(normalizado == null || await ReglasEmpleado.CorreoDisponibleAsync(_context, normalizado, null));
        }

        [HttpGet]
        public IActionResult Crear()
        {
            return View();
        }

        // [Bind] limita qué campos del formulario se copian al modelo: aunque alguien envíe
        // PasswordHash o IntentosFallidos manipulando el formulario, se ignoran
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Crear([Bind(nameof(Usuario.NombreCompleto), nameof(Usuario.Identificacion), nameof(Usuario.Correo),
            nameof(Usuario.RolId), nameof(Usuario.Estado))] Usuario model, string password, string confirmPassword)
        {
            // El formulario envía la contraseña en "password", no en "PasswordHash": el binding automático
            // la deja vacía y dispara el [Required] del modelo antes de llegar aquí. Se valida a mano abajo.
            ModelState.Remove("PasswordHash");

            if (string.IsNullOrWhiteSpace(password) || password.Length < 6)
            {
                ModelState.AddModelError(string.Empty, "La contraseña debe tener al menos 6 caracteres.");
            }
            else if (!System.Text.RegularExpressions.Regex.IsMatch(password, @"^(?=.*[A-Za-z])(?=.*\d).+$"))
            {
                ModelState.AddModelError(string.Empty, "La contraseña debe contener al menos una letra y un número.");
            }
            else if (password != confirmPassword)
            {
                ModelState.AddModelError(string.Empty, "Las contraseñas no coinciden.");
            }

            model.Correo = model.Correo.Trim().ToLower();
            if (await _context.Usuarios.AnyAsync(u => u.Correo == model.Correo))
            {
                ModelState.AddModelError("Correo", "Ya existe un usuario con este correo electrónico.");
            }
            else if (await _context.Empleados.AnyAsync(e => e.Correo == model.Correo))
            {
                ModelState.AddModelError("Correo", "Ese correo ya está en uso por un empleado registrado.");
            }

            if (!Roles.Catalogo.Any(r => r.Id == model.RolId))
            {
                ModelState.AddModelError("RolId", "Seleccione un rol válido.");
            }
            if (!EstadosUsuario.Todos.Contains(model.Estado))
            {
                ModelState.AddModelError("Estado", "Seleccione un estado válido.");
            }

            model.Identificacion = (model.Identificacion ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(model.Identificacion))
            {
                ModelState.AddModelError("Identificacion", "El número de identificación es obligatorio.");
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            // La contraseña se guarda como hash BCrypt, nunca en texto plano
            model.PasswordHash = BCrypt.Net.BCrypt.HashPassword(password);
            // "No verificado" hasta que escriba el código que se le envía la primera vez que inicia sesión
            model.CorreoVerificado = false;
            model.FechaRegistro = DateTime.Now;
            model.IntentosFallidos = 0;

            _context.Usuarios.Add(model);
            await _context.SaveChangesAsync();

            await _auditService.RegistrarAsync("Crear Usuario", "Usuarios", 
                $"Usuario creado: {model.NombreCompleto} con rol {Roles.Catalogo.First(r => r.Id == model.RolId).Nombre}");

            // TempData sobrevive a la redirección: _Layout lo muestra como alerta verde en la siguiente página
            TempData["SuccessMessage"] = $"Usuario '{model.NombreCompleto}' ({model.Correo}) creado. Verificará su correo con un código la primera vez que inicie sesión.";
            return RedirectToAction(nameof(Index));
        }

        // CU-04 y CU-05: Editar credenciales, roles y permisos
        [HttpGet]
        public async Task<IActionResult> Editar(int id)
        {
            var usuario = await _context.Usuarios.FindAsync(id);
            if (usuario == null) return NotFound();

            return View(usuario);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Editar(int id,
            [Required(ErrorMessage = "El nombre completo es obligatorio.")]
            [StringLength(100, ErrorMessage = "El nombre completo admite hasta {1} caracteres.")] string nombreCompleto,
            [StringLength(30, ErrorMessage = "La identificación admite hasta {1} dígitos.")]
            [RegularExpression(@"^\d*$", ErrorMessage = "La identificación solo debe contener números.")] string? identificacion,
            int rolId, string estado)
        {
            var usuario = await _context.Usuarios.FindAsync(id);
            if (usuario == null) return NotFound();

            if (!Roles.Catalogo.Any(r => r.Id == rolId) || !EstadosUsuario.Todos.Contains(estado))
            {
                ModelState.AddModelError(string.Empty, "El rol o el estado seleccionado no es válido.");
            }
            if (!ModelState.IsValid)
            {
                TempData["ErrorMessage"] = ModelState.PrimerError();
                return RedirectToAction(nameof(Editar), new { id });
            }

            // Quien edita es un Administrador activo (SesionUsuario lo comprueba en cada petición). Si no puede
            // cambiarse su propio rol ni estado, siempre queda al menos un Administrador con acceso al sistema.
            if (id == User.IdUsuario() && (rolId != usuario.RolId || estado != usuario.Estado))
            {
                TempData["ErrorMessage"] = "No puedes cambiar tu propio rol ni tu estado. Pídeselo a otro Administrador.";
                return RedirectToAction(nameof(Editar), new { id });
            }

            // RN-01-CU04: el Administrador solo puede editar nombre/identificación/rol/estado.
            // La contraseña únicamente puede ser cambiada por el propio usuario (Account/CambiarContrasena)
            // o mediante el flujo de aprobación de CU-41/CU-42. No se acepta aquí bajo ningún parámetro.
            usuario.NombreCompleto = nombreCompleto.Trim();
            usuario.Identificacion = (identificacion ?? string.Empty).Trim();
            usuario.RolId = rolId;
            usuario.Estado = estado;

            if (estado == EstadosUsuario.Activo)
            {
                usuario.IntentosFallidos = 0;
                usuario.FechaBloqueo = null;
            }

            // EF detecta qué propiedades cambiaron y ejecuta el UPDATE solo con esas columnas
            await _context.SaveChangesAsync();

            await _auditService.RegistrarAsync("Editar Usuario/Rol", "Usuarios", 
                $"Usuario {usuario.NombreCompleto} actualizado: Rol={Roles.Catalogo.First(r => r.Id == rolId).Nombre}, Estado={estado}");

            TempData["SuccessMessage"] = $"Usuario '{usuario.Correo}' actualizado exitosamente.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Desbloquear(int id)
        {
            var usuario = await _context.Usuarios.FindAsync(id);
            if (usuario == null) return NotFound();

            usuario.Estado = EstadosUsuario.Activo;
            usuario.IntentosFallidos = 0;
            usuario.FechaBloqueo = null;

            await _context.SaveChangesAsync();

            await _auditService.RegistrarAsync("Desbloquear Usuario", "Seguridad", 
                $"Cuenta de usuario {usuario.NombreCompleto} desbloqueada manualmente.");

            TempData["SuccessMessage"] = $"La cuenta de '{usuario.Correo}' ha sido desbloqueada.";
            return RedirectToAction(nameof(Index));
        }
    }
}