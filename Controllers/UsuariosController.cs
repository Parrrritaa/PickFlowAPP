using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickingApp.Data;
using PickingApp.Models;
using PickingApp.Services;

namespace PickingApp.Controllers
{
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

        // Listar usuarios del sistema
        public async Task<IActionResult> Index()
        {
            var usuarios = await _context.Usuarios
                .OrderBy(u => u.Rol)
                .ThenBy(u => u.NombreCompleto)
                .ToListAsync();

            return View(usuarios);
        }

        // Crear usuario
        [HttpGet]
        public IActionResult Crear()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Crear([Bind(nameof(Usuario.NombreCompleto), nameof(Usuario.Identificacion), nameof(Usuario.Correo),
            nameof(Usuario.Rol), nameof(Usuario.Estado))] Usuario model, string password, string confirmPassword)
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

            if (!Roles.Todos.Contains(model.Rol))
            {
                ModelState.AddModelError("Rol", "Seleccione un rol válido.");
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

            model.PasswordHash = BCrypt.Net.BCrypt.HashPassword(password);
            model.FechaRegistro = DateTime.Now;
            model.IntentosFallidos = 0;

            _context.Usuarios.Add(model);
            await _context.SaveChangesAsync();

            await _auditService.RegistrarAsync("Crear Usuario", "Usuarios", 
                $"Usuario creado: {model.NombreCompleto} con rol {model.Rol}");

            TempData["SuccessMessage"] = $"Usuario '{model.NombreCompleto}' ({model.Correo}) creado con éxito.";
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
        public async Task<IActionResult> Editar(int id, string nombreCompleto, string identificacion, string rol, string estado)
        {
            var usuario = await _context.Usuarios.FindAsync(id);
            if (usuario == null) return NotFound();

            if (!Roles.Todos.Contains(rol) || !EstadosUsuario.Todos.Contains(estado))
            {
                TempData["ErrorMessage"] = "El rol o el estado seleccionado no es válido.";
                return RedirectToAction(nameof(Editar), new { id });
            }

            // RN-01-CU04: el Administrador solo puede editar nombre/identificación/rol/estado.
            // La contraseña únicamente puede ser cambiada por el propio usuario (Account/CambiarContrasena)
            // o mediante el flujo de aprobación de CU-41/CU-42. No se acepta aquí bajo ningún parámetro.
            usuario.NombreCompleto = nombreCompleto.Trim();
            usuario.Identificacion = (identificacion ?? string.Empty).Trim();
            usuario.Rol = rol;
            usuario.Estado = estado;

            if (estado == EstadosUsuario.Activo)
            {
                usuario.IntentosFallidos = 0;
                usuario.FechaBloqueo = null;
            }

            await _context.SaveChangesAsync();

            await _auditService.RegistrarAsync("Editar Usuario/Rol", "Usuarios", 
                $"Usuario {usuario.NombreCompleto} actualizado: Rol={rol}, Estado={estado}");

            TempData["SuccessMessage"] = $"Usuario '{usuario.Correo}' actualizado exitosamente.";
            return RedirectToAction(nameof(Index));
        }

        // Desbloquear usuario
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