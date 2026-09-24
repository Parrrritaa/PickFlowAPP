using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickingApp.Data;
using PickingApp.Models;
using PickingApp.Services;

namespace PickingApp.Controllers
{
    // Categorías de manipulación y clasificación de productos (solo Administrador): definen el tiempo estándar de picking
    [Authorize(Roles = "Administrador")]
    public class ProductosController : Controller
    {
        private const double MaxMinutosPorUnidad = 600;

        private readonly ApplicationDbContext _context;
        private readonly IAuditService _auditService;

        public ProductosController(ApplicationDbContext context, IAuditService auditService)
        {
            _context = context;
            _auditService = auditService;
        }

        public async Task<IActionResult> Index()
        {
            ViewBag.Categorias = await _context.CategoriasProducto
                .Include(c => c.Productos)
                .OrderBy(c => c.MinutosPorUnidad)
                .ToListAsync();
            ViewBag.MinutosPorUbicacion = await ParametrosSistema.ObtenerDecimalAsync(_context, ParametrosSistema.MinutosPorUbicacion);

            var productos = await _context.Productos
                .Include(p => p.Categoria)
                .Include(p => p.Ubicaciones)
                .OrderBy(p => p.Codigo)
                .ToListAsync();

            return View(productos);
        }

        // Crear (id = null) o editar una categoría
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GuardarCategoria(int? id, string? nombre, string? minutosPorUnidad, string? descripcion)
        {
            nombre = (nombre ?? string.Empty).Trim();
            descripcion = string.IsNullOrWhiteSpace(descripcion) ? null : descripcion.Trim();

            if (string.IsNullOrEmpty(nombre) || nombre.Length > 50)
            {
                return Error("El nombre de la categoría es obligatorio y admite hasta 50 caracteres.");
            }
            if (descripcion != null && descripcion.Length > 150)
            {
                return Error("La descripción admite hasta 150 caracteres.");
            }
            if (!Formato.TryParseDecimal(minutosPorUnidad, out var minutos) || minutos <= 0 || minutos > MaxMinutosPorUnidad)
            {
                return Error($"Los minutos por unidad deben ser un número mayor que 0 y hasta {MaxMinutosPorUnidad:0}.");
            }

            var nombreNormalizado = nombre.ToLower();
            if (await _context.CategoriasProducto.AnyAsync(c => c.Nombre.ToLower() == nombreNormalizado && c.Id != id))
            {
                return Error($"Ya existe una categoría llamada '{nombre}'.");
            }

            CategoriaProducto? categoria;
            string accion;
            if (id.HasValue)
            {
                categoria = await _context.CategoriasProducto.FindAsync(id.Value);
                if (categoria == null) return Error("La categoría ya no existe.");
                accion = $"Categoría '{categoria.Nombre}' actualizada: {categoria.MinutosPorUnidad:0.##} → {minutos:0.##} min/u";
            }
            else
            {
                categoria = new CategoriaProducto();
                _context.CategoriasProducto.Add(categoria);
                accion = $"Categoría '{nombre}' creada con {minutos:0.##} min/u";
            }

            categoria.Nombre = nombre;
            categoria.MinutosPorUnidad = Math.Round(minutos, 2);
            categoria.Descripcion = descripcion;
            await _context.SaveChangesAsync();

            await RegistrarAsync(id.HasValue ? "Editar Categoría" : "Crear Categoría", accion);
            TempData["SuccessMessage"] = $"Categoría '{nombre}' guardada.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> HacerPredeterminada(int id)
        {
            var categoria = await _context.CategoriasProducto.FindAsync(id);
            if (categoria == null) return Error("La categoría ya no existe.");

            // Siempre hay exactamente una predeterminada
            await _context.CategoriasProducto.ForEachAsync(c => c.EsPredeterminada = c.Id == id);
            await _context.SaveChangesAsync();

            await RegistrarAsync("Categoría Predeterminada", $"'{categoria.Nombre}' es ahora la categoría para productos sin clasificar.");
            TempData["SuccessMessage"] = $"'{categoria.Nombre}' es ahora la categoría por defecto.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EliminarCategoria(int id)
        {
            var categoria = await _context.CategoriasProducto
                .Include(c => c.Productos)
                .FirstOrDefaultAsync(c => c.Id == id);
            if (categoria == null) return Error("La categoría ya no existe.");
            if (categoria.EsPredeterminada)
            {
                return Error("No se puede eliminar la categoría por defecto. Elige primero otra como predeterminada.");
            }

            var afectados = categoria.Productos.Count;
            // Los productos quedan sin categoría y pasan a usar la predeterminada
            foreach (var p in categoria.Productos) p.CategoriaProductoId = null;
            _context.CategoriasProducto.Remove(categoria);
            await _context.SaveChangesAsync();

            await RegistrarAsync("Eliminar Categoría", $"Categoría '{categoria.Nombre}' eliminada; {afectados} producto(s) pasaron a la categoría por defecto.");
            TempData["SuccessMessage"] = afectados > 0
                ? $"Categoría '{categoria.Nombre}' eliminada. Sus {afectados} producto(s) usan ahora la categoría por defecto."
                : $"Categoría '{categoria.Nombre}' eliminada.";
            return RedirectToAction(nameof(Index));
        }

        // Guarda la categoría de varios productos a la vez. Valor vacío = categoría por defecto.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GuardarClasificacion(Dictionary<int, string?> categoria)
        {
            var idsValidos = await _context.CategoriasProducto.Select(c => c.Id).ToListAsync();
            var ids = categoria.Keys.ToList();
            var productos = await _context.Productos.Where(p => ids.Contains(p.Id)).ToListAsync();

            var cambios = 0;
            foreach (var producto in productos)
            {
                int? nueva = int.TryParse(categoria[producto.Id], out var cid) && idsValidos.Contains(cid) ? cid : null;
                if (producto.CategoriaProductoId != nueva)
                {
                    producto.CategoriaProductoId = nueva;
                    cambios++;
                }
            }

            if (cambios > 0)
            {
                await _context.SaveChangesAsync();
                await RegistrarAsync("Clasificar Productos", $"Se actualizó la categoría de {cambios} producto(s).");
            }

            TempData["SuccessMessage"] = cambios == 1 ? "Se actualizó la categoría de 1 producto." : $"Se actualizó la categoría de {cambios} productos.";
            return RedirectToAction(nameof(Index));
        }

        private IActionResult Error(string mensaje)
        {
            TempData["ErrorMessage"] = mensaje;
            return RedirectToAction(nameof(Index));
        }

        private async Task RegistrarAsync(string accion, string detalle)
        {
            var userEmail = User.FindFirstValue(ClaimTypes.Email) ?? "Admin";
            await _auditService.LogAsync(userEmail, "Administrador", accion, "Productos", detalle);
        }
    }
}
