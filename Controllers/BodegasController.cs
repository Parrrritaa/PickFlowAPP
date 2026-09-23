using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickingApp.Data;
using PickingApp.Models;
using PickingApp.Services;

namespace PickingApp.Controllers
{
    [Authorize]
    public class BodegasController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IAuditService _auditService;

        public BodegasController(ApplicationDbContext context, IAuditService auditService)
        {
            _context = context;
            _auditService = auditService;
        }

        // CU-22: Consultar bodegas y ubicaciones
        public async Task<IActionResult> Index()
        {
            var bodegas = await _context.Bodegas
                .Include(b => b.Ubicaciones)
                .Include(b => b.Pedidos)
                .OrderBy(b => b.Codigo)
                .ToListAsync();

            return View(bodegas);
        }

        // Detalle de Bodega y sus Ubicaciones (Slide 26)
        public async Task<IActionResult> Detalle(int id)
        {
            var bodega = await _context.Bodegas
                .Include(b => b.Ubicaciones).ThenInclude(u => u.Producto)
                .FirstOrDefaultAsync(b => b.Id == id);

            if (bodega == null)
            {
                return NotFound();
            }

            ViewBag.TodasBodegas = await _context.Bodegas
                .Include(b => b.Ubicaciones)
                .ToListAsync();

            return View(bodega);
        }

        // CU-18: Crear bodega (Solo Administrador)
        [Authorize(Roles = "Administrador")]
        [HttpGet]
        public IActionResult Crear()
        {
            return View();
        }

        [Authorize(Roles = "Administrador")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Crear(Bodega model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            // El código de bodega siempre se genera automáticamente con autoincremento: BOD-01, BOD-02, ...
            var codigosExistentes = await _context.Bodegas.Select(b => b.Codigo).ToListAsync();
            var maxNumero = codigosExistentes
                .Select(c => System.Text.RegularExpressions.Regex.Match(c, @"BOD-(\d+)$"))
                .Where(m => m.Success)
                .Select(m => int.Parse(m.Groups[1].Value))
                .DefaultIfEmpty(0)
                .Max();
            model.Codigo = $"BOD-{(maxNumero + 1):D2}";

            model.FechaCreacion = DateTime.Now;
            _context.Bodegas.Add(model);
            await _context.SaveChangesAsync();

            var userEmail = User.FindFirstValue(ClaimTypes.Email) ?? "Admin";
            await _auditService.LogAsync(userEmail, "Administrador", "Crear Bodega", "Bodegas", 
                $"Bodega creada: {model.Codigo} - {model.Nombre}");

            TempData["SuccessMessage"] = $"Bodega '{model.Nombre}' ({model.Codigo}) creada exitosamente.";
            return RedirectToAction(nameof(Index));
        }

        // Editar bodega (Solo Administrador)
        [Authorize(Roles = "Administrador")]
        [HttpGet]
        public async Task<IActionResult> Editar(int id)
        {
            var bodega = await _context.Bodegas.FindAsync(id);
            if (bodega == null) return NotFound();

            return View(bodega);
        }

        [Authorize(Roles = "Administrador")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Editar(int id, Bodega model)
        {
            if (id != model.Id) return NotFound();

            var bodega = await _context.Bodegas.FindAsync(id);
            if (bodega == null) return NotFound();

            ModelState.Remove("Codigo");
            if (!ModelState.IsValid)
            {
                model.Codigo = bodega.Codigo;
                model.FechaCreacion = bodega.FechaCreacion;
                return View(model);
            }

            bodega.Nombre = model.Nombre.Trim();
            bodega.Descripcion = model.Descripcion?.Trim();
            bodega.Activa = model.Activa;
            await _context.SaveChangesAsync();

            var userEmail = User.FindFirstValue(ClaimTypes.Email) ?? "Admin";
            await _auditService.LogAsync(userEmail, "Administrador", "Editar Bodega", "Bodegas",
                $"Bodega {bodega.Codigo} editada: Nombre='{bodega.Nombre}', Activa={bodega.Activa}.");

            TempData["SuccessMessage"] = $"Bodega '{bodega.Nombre}' ({bodega.Codigo}) actualizada exitosamente.";
            return RedirectToAction(nameof(Index));
        }

        // Eliminar bodega (Solo Administrador)
        [Authorize(Roles = "Administrador")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EliminarBodega(int id)
        {
            var bodega = await _context.Bodegas
                .Include(b => b.Ubicaciones)
                .Include(b => b.Pedidos)
                .FirstOrDefaultAsync(b => b.Id == id);

            if (bodega == null) return NotFound();

            if (bodega.Ubicaciones.Any() || bodega.Pedidos.Any())
            {
                TempData["ErrorMessage"] = $"No se puede eliminar la bodega '{bodega.Nombre}' porque tiene ubicaciones y/o pedidos asociados.";
                return RedirectToAction(nameof(Index));
            }

            _context.Bodegas.Remove(bodega);
            await _context.SaveChangesAsync();

            var userEmail = User.FindFirstValue(ClaimTypes.Email) ?? "Admin";
            await _auditService.LogAsync(userEmail, "Administrador", "Eliminar Bodega", "Bodegas",
                $"Bodega {bodega.Codigo} - {bodega.Nombre} eliminada.");

            TempData["SuccessMessage"] = $"Bodega '{bodega.Nombre}' ({bodega.Codigo}) eliminada del sistema.";
            return RedirectToAction(nameof(Index));
        }

        // CU-19: Crear ubicación (Administrador y Supervisor)
        [Authorize(Roles = "Administrador,Supervisor")]
        [HttpGet]
        public async Task<IActionResult> CrearUbicacion(int bodegaId)
        {
            var bodega = await _context.Bodegas.FindAsync(bodegaId);
            if (bodega == null) return NotFound();

            ViewBag.Bodega = bodega;
            await CargarCategoriasAsync(null);
            var model = new Ubicacion { BodegaId = bodegaId };
            return View(model);
        }

        [Authorize(Roles = "Administrador,Supervisor")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CrearUbicacion(Ubicacion model, string numeroProducto, string nombreProducto, string? categoriaProductoId)
        {
            var bodega = await _context.Bodegas.FindAsync(model.BodegaId);
            if (bodega == null) return NotFound();
            ViewBag.Bodega = bodega;
            var categoriaId = await ResolverCategoriaAsync(categoriaProductoId);
            await CargarCategoriasAsync(categoriaId);

            numeroProducto = (numeroProducto ?? string.Empty).Trim();
            nombreProducto = (nombreProducto ?? string.Empty).Trim();
            ModelState.Remove("Producto");

            if (string.IsNullOrWhiteSpace(numeroProducto))
            {
                ModelState.AddModelError(string.Empty, "El número de producto es obligatorio.");
            }
            else if (!System.Text.RegularExpressions.Regex.IsMatch(numeroProducto, @"^\d+$") || !int.TryParse(numeroProducto, out _))
            {
                ModelState.AddModelError(string.Empty, "El número de producto solo debe contener números (máximo 10 dígitos).");
            }
            if (string.IsNullOrWhiteSpace(nombreProducto))
            {
                ModelState.AddModelError(string.Empty, "El nombre de producto es obligatorio.");
            }

            if (!ModelState.IsValid)
            {
                ViewBag.NumeroProducto = numeroProducto;
                ViewBag.NombreProducto = nombreProducto;
                return View(model);
            }

            // El código de producto siempre se genera automáticamente con el formato PROD-XXX a partir del número ingresado.
            var codigoProducto = $"PROD-{int.Parse(numeroProducto):D3}";

            // El código de ubicación siempre se genera automáticamente: PASXX-ESTXX-NIVXX
            model.Pasillo = model.Pasillo.Trim();
            model.Estante = model.Estante.Trim();
            model.Nivel = model.Nivel.Trim();
            model.CodigoUbicacion = $"PAS{int.Parse(model.Pasillo):D2}-EST{int.Parse(model.Estante):D2}-NIV{int.Parse(model.Nivel):D2}";

            if (await _context.Ubicaciones.AnyAsync(u => u.BodegaId == model.BodegaId && u.CodigoUbicacion == model.CodigoUbicacion))
            {
                ModelState.AddModelError("CodigoUbicacion", "Ya existe una ubicación con ese código en esta bodega.");
                ViewBag.NumeroProducto = numeroProducto;
                ViewBag.NombreProducto = nombreProducto;
                return View(model);
            }

            // El producto se reutiliza si ya existe con ese código; si no, se crea.
            var producto = await _context.Productos
                .FirstOrDefaultAsync(p => p.Codigo.ToUpper() == codigoProducto.ToUpper());
            if (producto == null)
            {
                producto = new Producto { Codigo = codigoProducto, Nombre = nombreProducto, CategoriaProductoId = categoriaId };
                _context.Productos.Add(producto);
            }
            else
            {
                producto.Nombre = nombreProducto;
                // Un producto existente conserva su categoría salvo que se elija otra
                if (categoriaId.HasValue) producto.CategoriaProductoId = categoriaId;
            }

            model.Producto = producto;
            _context.Ubicaciones.Add(model);
            await _context.SaveChangesAsync();

            var userEmail = User.FindFirstValue(ClaimTypes.Email) ?? "Usuario";
            var userRole = User.FindFirstValue(ClaimTypes.Role) ?? "Usuario";
            await _auditService.LogAsync(userEmail, userRole, "Crear Ubicación", "Ubicaciones",
                $"Ubicación creada en bodega {bodega.Codigo}: {model.CodigoUbicacion} (Producto: {producto.Codigo} - {producto.Nombre})");

            TempData["SuccessMessage"] = $"Ubicación '{model.CodigoUbicacion}' creada con éxito para el producto '{producto.Codigo}'.";
            return RedirectToAction(nameof(Detalle), new { id = model.BodegaId });
        }

        // Editar ubicación (Administrador: directo. Supervisor: genera solicitud que requiere aprobación)
        [Authorize(Roles = "Administrador,Supervisor")]
        [HttpGet]
        public async Task<IActionResult> EditarUbicacion(int id)
        {
            var ubicacion = await _context.Ubicaciones
                .Include(u => u.Bodega)
                .Include(u => u.Producto)
                .FirstOrDefaultAsync(u => u.Id == id);

            if (ubicacion == null) return NotFound();

            ViewBag.Bodega = ubicacion.Bodega;
            await CargarCategoriasAsync(ubicacion.Producto?.CategoriaProductoId);
            return View(ubicacion);
        }

        [Authorize(Roles = "Administrador,Supervisor")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditarUbicacion(int id, Ubicacion model, string numeroProducto, string nombreProducto, string? motivo, string? categoriaProductoId)
        {
            var ubicacionExistente = await _context.Ubicaciones
                .Include(u => u.Bodega)
                .Include(u => u.Producto)
                    .ThenInclude(p => p!.Categoria)
                .FirstOrDefaultAsync(u => u.Id == id);

            if (ubicacionExistente == null) return NotFound();
            ViewBag.Bodega = ubicacionExistente.Bodega;
            var categoriaId = await ResolverCategoriaAsync(categoriaProductoId);
            await CargarCategoriasAsync(categoriaId);

            ModelState.Remove("Producto");
            ModelState.Remove("CodigoUbicacion");

            numeroProducto = (numeroProducto ?? string.Empty).Trim();
            nombreProducto = (nombreProducto ?? string.Empty).Trim();
            motivo = (motivo ?? string.Empty).Trim();
            var userRole = User.FindFirstValue(ClaimTypes.Role) ?? "Usuario";

            if (string.IsNullOrWhiteSpace(numeroProducto))
            {
                ModelState.AddModelError(string.Empty, "El número de producto es obligatorio.");
            }
            else if (!System.Text.RegularExpressions.Regex.IsMatch(numeroProducto, @"^\d+$") || !int.TryParse(numeroProducto, out _))
            {
                ModelState.AddModelError(string.Empty, "El número de producto solo debe contener números (máximo 10 dígitos).");
            }
            if (string.IsNullOrWhiteSpace(nombreProducto))
            {
                ModelState.AddModelError(string.Empty, "El nombre de producto es obligatorio.");
            }
            if (userRole == "Supervisor" && string.IsNullOrWhiteSpace(motivo))
            {
                ModelState.AddModelError(string.Empty, "Debe indicar el motivo de la edición.");
            }

            if (!ModelState.IsValid)
            {
                ubicacionExistente.Pasillo = model.Pasillo;
                ubicacionExistente.Estante = model.Estante;
                ubicacionExistente.Nivel = model.Nivel;
                ubicacionExistente.Activa = model.Activa;
                ViewBag.NumeroProducto = numeroProducto;
                ViewBag.NombreProducto = nombreProducto;
                ViewBag.Motivo = motivo;
                return View(ubicacionExistente);
            }

            var codigoProductoNuevo = $"PROD-{int.Parse(numeroProducto):D3}";
            var pasillo = model.Pasillo.Trim();
            var estante = model.Estante.Trim();
            var nivel = model.Nivel.Trim();
            var codigoUbicacionNuevo = $"PAS{int.Parse(pasillo):D2}-EST{int.Parse(estante):D2}-NIV{int.Parse(nivel):D2}";

            if (codigoUbicacionNuevo != ubicacionExistente.CodigoUbicacion &&
                await _context.Ubicaciones.AnyAsync(u => u.BodegaId == ubicacionExistente.BodegaId && u.CodigoUbicacion == codigoUbicacionNuevo && u.Id != id))
            {
                ModelState.AddModelError(string.Empty, "Ya existe una ubicación con ese código en esta bodega.");
                ubicacionExistente.Pasillo = pasillo;
                ubicacionExistente.Estante = estante;
                ubicacionExistente.Nivel = nivel;
                ubicacionExistente.Activa = model.Activa;
                ViewBag.NumeroProducto = numeroProducto;
                ViewBag.NombreProducto = nombreProducto;
                ViewBag.Motivo = motivo;
                return View(ubicacionExistente);
            }

            var userEmail = User.FindFirstValue(ClaimTypes.Email) ?? "Usuario";
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var codigoProductoAnterior = ubicacionExistente.Producto?.Codigo ?? "-";
            var nombreProductoAnterior = ubicacionExistente.Producto?.Nombre ?? "-";

            if (userRole == "Administrador")
            {
                var producto = await _context.Productos
                    .FirstOrDefaultAsync(p => p.Codigo.ToUpper() == codigoProductoNuevo.ToUpper());
                if (producto == null)
                {
                    producto = new Producto { Codigo = codigoProductoNuevo, Nombre = nombreProducto };
                    _context.Productos.Add(producto);
                }
                else
                {
                    producto.Nombre = nombreProducto;
                }

                ubicacionExistente.Pasillo = pasillo;
                ubicacionExistente.Estante = estante;
                ubicacionExistente.Nivel = nivel;
                ubicacionExistente.CodigoUbicacion = codigoUbicacionNuevo;
                ubicacionExistente.Activa = model.Activa;
                ubicacionExistente.Producto = producto;
                producto.CategoriaProductoId = categoriaId;

                await _context.SaveChangesAsync();

                await _auditService.LogAsync(userEmail, userRole, "Editar Ubicación", "Ubicaciones",
                    $"Ubicación editada en bodega {ubicacionExistente.Bodega!.Codigo}: {codigoUbicacionNuevo} (Producto: {producto.Codigo} - {producto.Nombre})");

                TempData["SuccessMessage"] = $"Ubicación '{codigoUbicacionNuevo}' actualizada exitosamente.";
            }
            else
            {
                // Supervisor: solicitar edición de ubicación (requiere aprobación del Administrador)
                var cambios = new List<object>();
                void AgregarCambio(string campo, string anterior, string nuevo)
                {
                    if (!string.Equals(anterior, nuevo, StringComparison.Ordinal))
                    {
                        cambios.Add(new { Campo = campo, Anterior = anterior, Nuevo = nuevo });
                    }
                }

                AgregarCambio("Código de ubicación", ubicacionExistente.CodigoUbicacion, codigoUbicacionNuevo);
                AgregarCambio("Estado", ubicacionExistente.Activa ? "Activa" : "Inactiva", model.Activa ? "Activa" : "Inactiva");
                AgregarCambio("Código de producto", codigoProductoAnterior, codigoProductoNuevo);
                AgregarCambio("Nombre de producto", nombreProductoAnterior, nombreProducto);
                var nombreCategoriaNueva = categoriaId.HasValue
                    ? (await _context.CategoriasProducto.FindAsync(categoriaId.Value))?.Nombre ?? "Sin clasificar"
                    : "Sin clasificar";
                AgregarCambio("Categoría", ubicacionExistente.Producto?.Categoria?.Nombre ?? "Sin clasificar", nombreCategoriaNueva);

                var datosSolicitud = new
                {
                    UbicacionId = ubicacionExistente.Id,
                    BodegaId = ubicacionExistente.BodegaId,
                    BodegaCodigo = ubicacionExistente.Bodega?.Codigo,
                    Motivo = motivo,
                    Cambios = cambios,
                    NuevosDatos = new
                    {
                        Pasillo = pasillo,
                        Estante = estante,
                        Nivel = nivel,
                        CodigoUbicacion = codigoUbicacionNuevo,
                        Activa = model.Activa,
                        CodigoProducto = codigoProductoNuevo,
                        NombreProducto = nombreProducto,
                        CategoriaProductoId = categoriaId
                    }
                };

                var solicitud = new Solicitud
                {
                    TipoSolicitud = "EdicionUbicacion",
                    UsuarioSolicitanteId = userId,
                    Estado = "Pendiente",
                    FechaSolicitud = DateTime.Now,
                    DatosJson = System.Text.Json.JsonSerializer.Serialize(datosSolicitud)
                };

                _context.Solicitudes.Add(solicitud);
                _context.Notificaciones.Add(new Notificacion
                {
                    RolDestino = "Administrador",
                    Titulo = "Solicitud de Edición de Ubicación",
                    Mensaje = $"El {User.DescripcionParaNotificacion()} solicitó modificar la ubicación {ubicacionExistente.CodigoUbicacion}.",
                    Tipo = "Info",
                    FechaCreacion = DateTime.Now,
                    Enlace = "/Solicitudes/Index"
                });

                await _context.SaveChangesAsync();

                await _auditService.LogAsync(userEmail, userRole, "Solicitud Edición Ubicación", "Solicitudes",
                    $"Solicitud de edición enviada para ubicación {ubicacionExistente.CodigoUbicacion}. Motivo: {motivo}");

                TempData["SuccessMessage"] = $"Se ha generado la solicitud de modificación para la ubicación '{ubicacionExistente.CodigoUbicacion}'. Requiere aprobación del Administrador.";
            }

            return RedirectToAction(nameof(Detalle), new { id = ubicacionExistente.BodegaId });
        }

        // CU-20 y CU-21: Eliminar o Solicitar Eliminación de Ubicación
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EliminarUbicacion(int id, string? motivo)
        {
            var ubicacion = await _context.Ubicaciones
                .Include(u => u.Bodega)
                .Include(u => u.DetallesPedido)
                .FirstOrDefaultAsync(u => u.Id == id);

            if (ubicacion == null) return NotFound();

            var userEmail = User.FindFirstValue(ClaimTypes.Email) ?? "Usuario";
            var userRole = User.FindFirstValue(ClaimTypes.Role) ?? "Usuario";
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            // Si tiene pedidos asociados, no se puede eliminar
            if (ubicacion.DetallesPedido.Any())
            {
                TempData["ErrorMessage"] = "No se puede eliminar la ubicación porque tiene pedidos asociados históricos.";
                return RedirectToAction(nameof(Detalle), new { id = ubicacion.BodegaId });
            }

            // Administrador: Eliminación Directa
            if (userRole == "Administrador")
            {
                _context.Ubicaciones.Remove(ubicacion);
                await _context.SaveChangesAsync();

                await _auditService.LogAsync(userEmail, userRole, "Eliminar Ubicación", "Ubicaciones", 
                    $"Ubicación {ubicacion.CodigoUbicacion} eliminada directamente por el Administrador.");

                TempData["SuccessMessage"] = $"Ubicación '{ubicacion.CodigoUbicacion}' eliminada del sistema.";
            }
            // Supervisor: Solicitar Eliminación (CU-20)
            else if (userRole == "Supervisor")
            {
                var solicitud = new Solicitud
                {
                    TipoSolicitud = "EliminacionUbicacion",
                    UsuarioSolicitanteId = userId,
                    Estado = "Pendiente",
                    FechaSolicitud = DateTime.Now,
                    DatosJson = System.Text.Json.JsonSerializer.Serialize(new
                    {
                        UbicacionId = ubicacion.Id,
                        CodigoUbicacion = ubicacion.CodigoUbicacion,
                        BodegaCodigo = ubicacion.Bodega?.Codigo,
                        Motivo = motivo ?? "Solicitud de eliminación por cambio operativo"
                    })
                };

                _context.Solicitudes.Add(solicitud);
                _context.Notificaciones.Add(new Notificacion
                {
                    RolDestino = "Administrador",
                    Titulo = "Solicitud de Eliminación de Ubicación",
                    Mensaje = $"El {User.DescripcionParaNotificacion()} solicitó eliminar la ubicación {ubicacion.CodigoUbicacion}.",
                    Tipo = "Warning",
                    FechaCreacion = DateTime.Now,
                    Enlace = "/Solicitudes/Index"
                });

                await _context.SaveChangesAsync();

                await _auditService.LogAsync(userEmail, userRole, "Solicitud Eliminación Ubicación", "Solicitudes", 
                    $"Supervisor solicitó eliminación de ubicación {ubicacion.CodigoUbicacion}. Motivo: {motivo}");

                TempData["SuccessMessage"] = $"Se ha generado la solicitud de eliminación para la ubicación '{ubicacion.CodigoUbicacion}'. Requiere aprobación del Administrador.";
            }
            else
            {
                TempData["ErrorMessage"] = "No tienes permisos para realizar o solicitar esta acción.";
            }

            return RedirectToAction(nameof(Detalle), new { id = ubicacion.BodegaId });
        }

        // Lista de categorías para el formulario de ubicación
        private async Task CargarCategoriasAsync(int? seleccionada)
        {
            ViewBag.Categorias = await _context.CategoriasProducto.OrderBy(c => c.MinutosPorUnidad).ToListAsync();
            ViewBag.CategoriaSeleccionada = seleccionada?.ToString() ?? "";
        }

        // Valor del formulario → Id de categoría existente, o null (sin clasificar)
        private async Task<int?> ResolverCategoriaAsync(string? valor)
        {
            if (!int.TryParse(valor, out var id)) return null;
            return await _context.CategoriasProducto.AnyAsync(c => c.Id == id) ? id : null;
        }
    }
}
