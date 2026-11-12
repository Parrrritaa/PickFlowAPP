using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickingApp.Data;
using PickingApp.Models;
using PickingApp.Services;

namespace PickingApp.Controllers
{
    // Gestión de usuarios del sistema. [Authorize(Roles = ...)] en la clase aplica a todas sus acciones.
    // El Administrador gestiona a todos; el Supervisor solo a los auxiliares de su bodega (DelAlcance).
    [Authorize(Roles = Roles.AdministradorOSupervisor)]
    public class UsuariosController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IAuditService _auditService;

        public UsuariosController(ApplicationDbContext context, IAuditService auditService)
        {
            _context = context;
            _auditService = auditService;
        }

        private bool EsAdministrador => User.IsInRole(Roles.Administrador);

        // Al Supervisor, un usuario que no es auxiliar de su bodega le responde 404, como si no existiera
        private IQueryable<Usuario> DelAlcance()
        {
            if (EsAdministrador) return _context.Usuarios;
            var bodegaId = User.IdBodega();
            return _context.Usuarios.Where(u => u.RolId == Roles.IdAuxiliar && u.BodegaId == bodegaId);
        }

        // La identificación es de una sola persona: dos usuarios no pueden compartirla (también la cuida el índice
        // único de ApplicationDbContext). usuarioId excluye al que se edita. Un usuario y un empleado sí pueden
        // tenerla igual: es la misma persona con cuenta en el sistema.
        private async Task<string?> ErrorIdentificacionAsync(string identificacion, int? usuarioId, int? bodegaId)
        {
            var registrado = await _context.Usuarios.Where(u => u.Identificacion == identificacion && u.Id != usuarioId)
                .Select(u => new { u.BodegaId }).FirstOrDefaultAsync();
            if (registrado == null) return null;
            return !EsAdministrador && registrado.BodegaId != null && registrado.BodegaId != bodegaId
                ? "Esa identificación pertenece a un usuario de otra bodega. Si se trasladó a la tuya, pide al Administrador que le cambie la bodega."
                : "Ya existe un usuario con este número de identificación.";
        }

        // El Administrador no tiene bodega; Supervisor y Auxiliar necesitan una activa
        // (o conservar la que ya tenían, aunque se haya inactivado después).
        private async Task<string?> ErrorBodegaAsync(int rolId, int? bodegaId, int? bodegaActual)
        {
            if (rolId == Roles.IdAdministrador) return null;
            if (bodegaId == null) return "Elige la bodega donde trabajará.";
            return await _context.Bodegas.AnyAsync(b => b.Id == bodegaId && (b.Activa || b.Id == bodegaActual))
                ? null : "Elige una bodega activa.";
        }

        public async Task<IActionResult> Index()
        {
            var usuarios = await DelAlcance()
                .Include(u => u.Bodega)
                .OrderBy(u => u.Rol!.Nombre)
                .ThenBy(u => u.NombreCompleto)
                .ToListAsync();

            if (!EsAdministrador)
            {
                ViewData["Trasladados"] = await Traslados.AuxiliaresQueSalieronAsync(_context, User.BodegaVisible()!.Value);
            }
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
            nameof(Usuario.RolId), nameof(Usuario.Estado), nameof(Usuario.BodegaId))] Usuario model, string password, string confirmPassword)
        {
            if (!EsAdministrador)
            {
                // El Supervisor solo crea auxiliares de su bodega: rol y bodega se fijan aquí, no vienen del formulario
                model.RolId = Roles.IdAuxiliar;
                model.BodegaId = User.IdBodega();
            }
            else if (model.RolId == Roles.IdAdministrador)
            {
                model.BodegaId = null;
            }

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
            var registrado = await _context.Usuarios.Where(u => u.Correo == model.Correo).Select(u => new { u.BodegaId }).FirstOrDefaultAsync();
            if (registrado != null)
            {
                ModelState.AddModelError("Correo", !EsAdministrador && registrado.BodegaId != null && registrado.BodegaId != model.BodegaId
                    ? "Ese correo pertenece a un usuario de otra bodega. Si se trasladó a la tuya, pide al Administrador que le cambie la bodega."
                    : "Ya existe un usuario con este correo electrónico.");
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
            var errorBodega = await ErrorBodegaAsync(model.RolId, model.BodegaId, null);
            if (errorBodega != null)
            {
                ModelState.AddModelError("BodegaId", errorBodega);
            }

            model.Identificacion = (model.Identificacion ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(model.Identificacion))
            {
                ModelState.AddModelError("Identificacion", "El número de identificación es obligatorio.");
            }
            else if (await ErrorIdentificacionAsync(model.Identificacion, null, model.BodegaId) is string errorIdentificacion)
            {
                ModelState.AddModelError("Identificacion", errorIdentificacion);
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
            var usuario = await DelAlcance().FirstOrDefaultAsync(u => u.Id == id);
            if (usuario == null) return NotFound();

            ViewData["Periodos"] = Traslados.Periodos(await Traslados.DeUsuarioAsync(_context, id), usuario.FechaRegistro);
            return View(usuario);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Editar(int id,
            [Required(ErrorMessage = "El nombre completo es obligatorio.")]
            [StringLength(100, ErrorMessage = "El nombre completo admite hasta {1} caracteres.")] string nombreCompleto,
            [StringLength(30, ErrorMessage = "La identificación admite hasta {1} dígitos.")]
            [RegularExpression(@"^\d*$", ErrorMessage = "La identificación solo debe contener números.")] string? identificacion,
            int rolId, string estado, int? bodegaId)
        {
            var usuario = await DelAlcance().FirstOrDefaultAsync(u => u.Id == id);
            if (usuario == null) return NotFound();

            if (!EsAdministrador)
            {
                // El Supervisor no cambia rol ni bodega: solo el Administrador asciende o traslada
                rolId = usuario.RolId;
                bodegaId = usuario.BodegaId;
            }
            else if (rolId == Roles.IdAdministrador)
            {
                bodegaId = null;
            }

            if (!Roles.Catalogo.Any(r => r.Id == rolId) || !EstadosUsuario.Todos.Contains(estado))
            {
                ModelState.AddModelError(string.Empty, "El rol o el estado seleccionado no es válido.");
            }
            var errorBodega = await ErrorBodegaAsync(rolId, bodegaId, usuario.BodegaId);
            if (errorBodega != null)
            {
                ModelState.AddModelError(string.Empty, errorBodega);
            }
            identificacion = (identificacion ?? string.Empty).Trim();
            if (identificacion.Length == 0)
            {
                ModelState.AddModelError(string.Empty, "El número de identificación es obligatorio.");
            }
            else if (await ErrorIdentificacionAsync(identificacion, id, bodegaId) is string errorIdentificacion)
            {
                ModelState.AddModelError(string.Empty, errorIdentificacion);
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
            usuario.Identificacion = identificacion;
            usuario.RolId = rolId;
            usuario.Estado = estado;
            Traslados.CambiarBodega(_context, usuario, bodegaId);

            if (estado == EstadosUsuario.Activo)
            {
                usuario.IntentosFallidos = 0;
                usuario.FechaBloqueo = null;
            }

            // EF detecta qué propiedades cambiaron y ejecuta el UPDATE solo con esas columnas
            await _context.SaveChangesAsync();

            await _auditService.RegistrarAsync("Editar Usuario/Rol", "Usuarios", 
                $"Usuario {usuario.NombreCompleto} actualizado: Rol={Roles.Catalogo.First(r => r.Id == rolId).Nombre}, Estado={estado}, " +
                $"Bodega={(bodegaId == null ? "ninguna" : (await _context.Bodegas.FindAsync(bodegaId))!.Nombre)}");

            TempData["SuccessMessage"] = $"Usuario '{usuario.Correo}' actualizado exitosamente.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Desbloquear(int id)
        {
            var usuario = await DelAlcance().FirstOrDefaultAsync(u => u.Id == id);
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