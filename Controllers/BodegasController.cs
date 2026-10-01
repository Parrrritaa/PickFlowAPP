using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickingApp.Data;
using PickingApp.Models;
using PickingApp.Models.ViewModels;
using PickingApp.Services;

namespace PickingApp.Controllers
{
    // Bodegas y sus ubicaciones (pasillo/estante/nivel). Cada ubicación guarda un solo producto.
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
        public async Task<IActionResult> Index(string? estado, string? busquedaProducto)
        {
            var query = _context.Bodegas.AsQueryable();

            if (estado == "Activa")
            {
                query = query.Where(b => b.Activa);
            }
            else if (estado == "Inactiva")
            {
                query = query.Where(b => !b.Activa);
            }

            var bodegas = await query.OrderBy(b => b.Codigo).ToListAsync();

            ViewBag.UbicacionesPorBodega = await ContarUbicacionesPorBodegaAsync();

            // Búsqueda de un producto específico: en qué bodega está y qué ubicación tiene
            List<Ubicacion>? resultadosProducto = null;
            if (!string.IsNullOrWhiteSpace(busquedaProducto))
            {
                var texto = busquedaProducto.Trim().ToLower();
                resultadosProducto = await _context.Ubicaciones
                    .Include(u => u.Bodega)
                    .Include(u => u.Producto)
                    .Where(u => u.Producto != null &&
                                (u.Producto.Codigo.ToLower().Contains(texto) || u.Producto.Nombre.ToLower().Contains(texto)))
                    .OrderBy(u => u.Bodega!.Codigo).ThenBy(u => u.CodigoUbicacion)
                    .ToListAsync();
            }

            ViewBag.FiltroEstado = estado;
            ViewBag.BusquedaProducto = busquedaProducto;
            ViewBag.ResultadosProducto = resultadosProducto;

            return View(bodegas);
        }

        // Las listas de bodegas solo muestran cuántas ubicaciones tiene cada una: se cuentan en SQL en vez de cargarlas
        private Task<Dictionary<int, int>> ContarUbicacionesPorBodegaAsync() =>
            _context.Ubicaciones
                .GroupBy(u => u.BodegaId)
                .Select(g => new { BodegaId = g.Key, Total = g.Count() })
                .ToDictionaryAsync(x => x.BodegaId, x => x.Total);

        // Detalle de Bodega y sus Ubicaciones (diapositiva 26)
        public async Task<IActionResult> Detalle(int id, string? filtroUbicacion, string? filtroProducto)
        {
            // AsNoTracking: vista de solo lectura; más abajo se reemplaza bodega.Ubicaciones con la lista filtrada.
            var bodega = await _context.Bodegas
                .Include(b => b.Ubicaciones).ThenInclude(u => u.Producto)
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.Id == id);

            if (bodega == null)
            {
                return NotFound();
            }

            IEnumerable<Ubicacion> ubicaciones = bodega.Ubicaciones.OrderBy(u => u.CodigoUbicacion);

            if (!string.IsNullOrWhiteSpace(filtroUbicacion))
            {
                var texto = filtroUbicacion.Trim().ToLower();
                ubicaciones = ubicaciones.Where(u => u.CodigoUbicacion.ToLower().Contains(texto));
            }

            if (!string.IsNullOrWhiteSpace(filtroProducto))
            {
                var texto = filtroProducto.Trim().ToLower();
                ubicaciones = ubicaciones.Where(u => u.Producto != null &&
                    (u.Producto.Codigo.ToLower().Contains(texto) || u.Producto.Nombre.ToLower().Contains(texto)));
            }

            bodega.Ubicaciones = ubicaciones.ToList();

            ViewBag.TodasBodegas = await _context.Bodegas.AsNoTracking().ToListAsync();
            ViewBag.UbicacionesPorBodega = await ContarUbicacionesPorBodegaAsync();
            ViewBag.FiltroUbicacion = filtroUbicacion;
            ViewBag.FiltroProducto = filtroProducto;

            return View(bodega);
        }

        // CU-18: Crear bodega (Solo Administrador)
        [Authorize(Roles = Roles.Administrador)]
        [HttpGet]
        public IActionResult Crear()
        {
            return View();
        }

        [Authorize(Roles = Roles.Administrador)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Crear([Bind(nameof(Bodega.Nombre), nameof(Bodega.Descripcion), nameof(Bodega.Activa))] Bodega model)
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

            // INSERT de la nueva bodega en MySQL
            model.FechaCreacion = DateTime.Now;
            _context.Bodegas.Add(model);
            await _context.SaveChangesAsync();

            await _auditService.RegistrarAsync("Crear Bodega", "Bodegas", 
                $"Bodega creada: {model.Codigo} - {model.Nombre}");

            TempData["SuccessMessage"] = $"Bodega '{model.Nombre}' ({model.Codigo}) creada exitosamente.";
            return RedirectToAction(nameof(Index));
        }

        // Editar bodega (Solo Administrador)
        [Authorize(Roles = Roles.Administrador)]
        [HttpGet]
        public async Task<IActionResult> Editar(int id)
        {
            var bodega = await _context.Bodegas.FindAsync(id);
            if (bodega == null) return NotFound();

            return View(bodega);
        }

        [Authorize(Roles = Roles.Administrador)]
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

            await _auditService.RegistrarAsync("Editar Bodega", "Bodegas",
                $"Bodega {bodega.Codigo} editada: Nombre='{bodega.Nombre}', Activa={bodega.Activa}.");

            TempData["SuccessMessage"] = $"Bodega '{bodega.Nombre}' ({bodega.Codigo}) actualizada exitosamente.";
            return RedirectToAction(nameof(Index));
        }

        // Eliminar bodega (Solo Administrador)
        [Authorize(Roles = Roles.Administrador)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EliminarBodega(int id)
        {
            var bodega = await _context.Bodegas.FindAsync(id);
            if (bodega == null) return NotFound();

            // Se valida antes de borrar: la relación es Restrict y MySQL rechazaría el DELETE
            if (await _context.Ubicaciones.AnyAsync(u => u.BodegaId == id) || await _context.Pedidos.AnyAsync(p => p.BodegaId == id))
            {
                TempData["ErrorMessage"] = $"No se puede eliminar la bodega '{bodega.Nombre}' porque tiene ubicaciones y/o pedidos asociados.";
                return RedirectToAction(nameof(Index));
            }

            _context.Bodegas.Remove(bodega);
            await _context.SaveChangesAsync();

            await _auditService.RegistrarAsync("Eliminar Bodega", "Bodegas",
                $"Bodega {bodega.Codigo} - {bodega.Nombre} eliminada.");

            TempData["SuccessMessage"] = $"Bodega '{bodega.Nombre}' ({bodega.Codigo}) eliminada del sistema.";
            return RedirectToAction(nameof(Index));
        }

        // CU-19: Crear ubicación (Administrador y Supervisor)
        [Authorize(Roles = Roles.AdministradorOSupervisor)]
        [HttpGet]
        public async Task<IActionResult> CrearUbicacion(int bodegaId)
        {
            var bodega = await _context.Bodegas.FindAsync(bodegaId);
            if (bodega == null) return NotFound();

            ViewBag.Bodega = bodega;
            await PrepararSeleccionProductoAsync(new SeleccionProductoViewModel());
            var model = new Ubicacion { BodegaId = bodegaId };
            return View(model);
        }

        // Los datos del producto llegan en "seleccion" (campos seleccion.Modo, seleccion.ProductoId, ...)
        [Authorize(Roles = Roles.AdministradorOSupervisor)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CrearUbicacion([Bind(nameof(Ubicacion.BodegaId), nameof(Ubicacion.Pasillo), nameof(Ubicacion.Estante),
            nameof(Ubicacion.Nivel), nameof(Ubicacion.Activa))] Ubicacion model, SeleccionProductoViewModel seleccion)
        {
            var bodega = await _context.Bodegas.FindAsync(model.BodegaId);
            if (bodega == null) return NotFound();
            ViewBag.Bodega = bodega;
            ModelState.Remove("Producto");

            var producto = await ResolverProductoAsync(seleccion);
            if (!ModelState.IsValid || producto == null)
            {
                await PrepararSeleccionProductoAsync(seleccion);
                return View(model);
            }

            // El código de ubicación siempre se genera automáticamente: PASXX-ESTXX-NIVXX
            model.Pasillo = model.Pasillo.Trim();
            model.Estante = model.Estante.Trim();
            model.Nivel = model.Nivel.Trim();
            model.CodigoUbicacion = Ubicacion.GenerarCodigo(model.Pasillo, model.Estante, model.Nivel);

            if (await _context.Ubicaciones.AnyAsync(u => u.BodegaId == model.BodegaId && u.CodigoUbicacion == model.CodigoUbicacion))
            {
                ModelState.AddModelError("CodigoUbicacion", "Ya existe una ubicación con ese código en esta bodega.");
                await PrepararSeleccionProductoAsync(seleccion);
                return View(model);
            }

            // Producto nuevo: se inserta junto con la ubicación en el mismo SaveChanges
            var productoNuevo = producto.Id == 0;
            if (productoNuevo) _context.Productos.Add(producto);

            model.Producto = producto;
            _context.Ubicaciones.Add(model);
            await _context.SaveChangesAsync();

            await _auditService.RegistrarAsync("Crear Ubicación", "Ubicaciones",
                $"Ubicación creada en bodega {bodega.Codigo}: {model.CodigoUbicacion} ({(productoNuevo ? "producto nuevo" : "Producto")}: {producto.Codigo} - {producto.Nombre})");

            TempData["SuccessMessage"] = productoNuevo
                ? $"Ubicación '{model.CodigoUbicacion}' creada con el producto nuevo '{producto.Codigo}'."
                : $"Ubicación '{model.CodigoUbicacion}' creada para el producto '{producto.Codigo}'.";
            return RedirectToAction(nameof(Detalle), new { id = model.BodegaId });
        }

        // Editar ubicación (Administrador: directo. Supervisor: genera solicitud que requiere aprobación)
        [Authorize(Roles = Roles.AdministradorOSupervisor)]
        [HttpGet]
        public async Task<IActionResult> EditarUbicacion(int id)
        {
            var ubicacion = await _context.Ubicaciones
                .Include(u => u.Bodega)
                .Include(u => u.Producto)
                .FirstOrDefaultAsync(u => u.Id == id);

            if (ubicacion == null) return NotFound();

            ViewBag.Bodega = ubicacion.Bodega;
            await PrepararSeleccionProductoAsync(new SeleccionProductoViewModel { ProductoId = ubicacion.ProductoId });
            return View(ubicacion);
        }

        [Authorize(Roles = Roles.AdministradorOSupervisor)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditarUbicacion(int id, Ubicacion model, SeleccionProductoViewModel seleccion, string? motivo)
        {
            var ubicacionExistente = await _context.Ubicaciones
                .Include(u => u.Bodega)
                .Include(u => u.Producto)
                .FirstOrDefaultAsync(u => u.Id == id);

            if (ubicacionExistente == null) return NotFound();
            ViewBag.Bodega = ubicacionExistente.Bodega;

            ModelState.Remove("Producto");
            ModelState.Remove("CodigoUbicacion");

            motivo = (motivo ?? string.Empty).Trim();
            var userRole = User.FindFirstValue(ClaimTypes.Role) ?? "Usuario";

            if (userRole == Roles.Supervisor && string.IsNullOrWhiteSpace(motivo))
            {
                ModelState.AddModelError(string.Empty, "Debe indicar el motivo de la edición.");
            }

            // Vuelve a mostrar el formulario con lo que escribió el usuario (no se guarda nada)
            async Task<IActionResult> Reintentar()
            {
                ubicacionExistente.Pasillo = model.Pasillo;
                ubicacionExistente.Estante = model.Estante;
                ubicacionExistente.Nivel = model.Nivel;
                ubicacionExistente.Activa = model.Activa;
                ViewBag.Motivo = motivo;
                await PrepararSeleccionProductoAsync(seleccion);
                return View(ubicacionExistente);
            }

            var producto = await ResolverProductoAsync(seleccion);
            if (!ModelState.IsValid || producto == null)
            {
                return await Reintentar();
            }

            var pasillo = model.Pasillo.Trim();
            var estante = model.Estante.Trim();
            var nivel = model.Nivel.Trim();
            var codigoUbicacionNuevo = Ubicacion.GenerarCodigo(pasillo, estante, nivel);

            if (codigoUbicacionNuevo != ubicacionExistente.CodigoUbicacion &&
                await _context.Ubicaciones.AnyAsync(u => u.BodegaId == ubicacionExistente.BodegaId && u.CodigoUbicacion == codigoUbicacionNuevo && u.Id != id))
            {
                ModelState.AddModelError(string.Empty, "Ya existe una ubicación con ese código en esta bodega.");
                return await Reintentar();
            }

            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var productoNuevo = producto.Id == 0;

            if (userRole == Roles.Administrador)
            {
                if (productoNuevo) _context.Productos.Add(producto);

                ubicacionExistente.Pasillo = pasillo;
                ubicacionExistente.Estante = estante;
                ubicacionExistente.Nivel = nivel;
                ubicacionExistente.CodigoUbicacion = codigoUbicacionNuevo;
                ubicacionExistente.Activa = model.Activa;
                ubicacionExistente.Producto = producto;

                await _context.SaveChangesAsync();

                await _auditService.RegistrarAsync("Editar Ubicación", "Ubicaciones",
                    $"Ubicación editada en bodega {ubicacionExistente.Bodega!.Codigo}: {codigoUbicacionNuevo} ({(productoNuevo ? "producto nuevo" : "Producto")}: {producto.Codigo} - {producto.Nombre})");

                TempData["SuccessMessage"] = $"Ubicación '{codigoUbicacionNuevo}' actualizada exitosamente.";
            }
            else
            {
                // Supervisor: solicitar edición de ubicación (requiere aprobación del Administrador).
                // El producto nuevo no se crea todavía: sus datos viajan en la solicitud.
                var cambios = new List<CambioSolicitud>();
                void AgregarCambio(string campo, string anterior, string nuevo)
                {
                    if (!string.Equals(anterior, nuevo, StringComparison.Ordinal))
                    {
                        cambios.Add(new CambioSolicitud { Campo = campo, Anterior = anterior, Nuevo = nuevo });
                    }
                }

                var productoAnterior = ubicacionExistente.Producto;
                var textoProductoAnterior = productoAnterior != null ? $"{productoAnterior.Codigo} · {productoAnterior.Nombre}" : "-";
                AgregarCambio(CamposUbicacion.Codigo, ubicacionExistente.CodigoUbicacion, codigoUbicacionNuevo);
                AgregarCambio(CamposUbicacion.Estado, ubicacionExistente.Activa ? "Activa" : "Inactiva", model.Activa ? "Activa" : "Inactiva");
                AgregarCambio(CamposUbicacion.Producto, textoProductoAnterior,
                    $"{producto.Codigo} · {producto.Nombre}{(productoNuevo ? " (nuevo)" : "")}");
                if (productoNuevo)
                {
                    var nombreCategoria = producto.CategoriaProductoId.HasValue
                        ? (await _context.CategoriasProducto.FindAsync(producto.CategoriaProductoId.Value))?.Nombre ?? "Sin clasificar"
                        : "Sin clasificar";
                    AgregarCambio(CamposUbicacion.CategoriaProductoNuevo, "-", nombreCategoria);
                    AgregarCambio(CamposUbicacion.UnidadesProductoNuevo, "-", producto.UnidadesPorCaja.ToString());
                }

                // Sin cambios no hay nada que aprobar; una segunda solicitud sobre la misma ubicación
                // mostraría valores "anteriores" que ya no serían los reales al aprobarla
                var pendiente = await SolicitudesPendientes.DeUbicacionAsync(_context, ubicacionExistente.Id);
                if (cambios.Count == 0 || pendiente != null)
                {
                    ModelState.AddModelError(string.Empty, cambios.Count == 0
                        ? "No cambió ningún dato de la ubicación: no hay nada que solicitar."
                        : SolicitudesPendientes.Mensaje(pendiente!, "esta ubicación"));
                    return await Reintentar();
                }

                var solicitud = Solicitud.Nueva(TiposSolicitud.EdicionUbicacion, userId, new SolicitudEdicionUbicacion
                {
                    UbicacionId = ubicacionExistente.Id,
                    BodegaId = ubicacionExistente.BodegaId,
                    BodegaCodigo = ubicacionExistente.Bodega?.Codigo,
                    CodigoUbicacion = ubicacionExistente.CodigoUbicacion,
                    Producto = productoAnterior != null ? textoProductoAnterior : "Sin producto",
                    Motivo = motivo,
                    Cambios = cambios,
                    NuevosDatos = new DatosUbicacionSolicitud
                    {
                        Pasillo = pasillo,
                        Estante = estante,
                        Nivel = nivel,
                        CodigoUbicacion = codigoUbicacionNuevo,
                        Activa = model.Activa,
                        ProductoId = productoNuevo ? null : producto.Id,
                        ProductoNuevo = productoNuevo,
                        CodigoProducto = producto.Codigo,
                        NombreProducto = producto.Nombre,
                        CategoriaProductoId = producto.CategoriaProductoId,
                        UnidadesPorCaja = producto.UnidadesPorCaja
                    }
                });

                _context.Solicitudes.Add(solicitud);
                _context.Notificaciones.Add(new Notificacion
                {
                    RolDestinoId = Roles.IdAdministrador,
                    Titulo = "Solicitud de Edición de Ubicación",
                    Mensaje = $"El {User.DescripcionParaNotificacion()} solicitó modificar la ubicación {ubicacionExistente.CodigoUbicacion}.",
                    Tipo = TiposNotificacion.Info,
                    FechaCreacion = DateTime.Now,
                    Enlace = "/Solicitudes/Index"
                });

                await _context.SaveChangesAsync();

                await _auditService.RegistrarAsync("Solicitud Edición Ubicación", "Solicitudes",
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
                .Include(u => u.Producto)
                .Include(u => u.DetallesPedido)
                .FirstOrDefaultAsync(u => u.Id == id);

            if (ubicacion == null) return NotFound();

            var userRole = User.FindFirstValue(ClaimTypes.Role) ?? "Usuario";
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            // Si tiene pedidos asociados, no se puede eliminar
            if (ubicacion.DetallesPedido.Any())
            {
                TempData["ErrorMessage"] = "No se puede eliminar la ubicación porque tiene pedidos asociados históricos.";
                return RedirectToAction(nameof(Detalle), new { id = ubicacion.BodegaId });
            }

            // Administrador: Eliminación Directa
            if (userRole == Roles.Administrador)
            {
                _context.Ubicaciones.Remove(ubicacion);
                await _context.SaveChangesAsync();

                await _auditService.RegistrarAsync("Eliminar Ubicación", "Ubicaciones", 
                    $"Ubicación {ubicacion.CodigoUbicacion} eliminada directamente por el Administrador.");

                TempData["SuccessMessage"] = $"Ubicación '{ubicacion.CodigoUbicacion}' eliminada del sistema.";
            }
            // Supervisor: Solicitar Eliminación (CU-20)
            else if (userRole == Roles.Supervisor)
            {
                motivo = (motivo ?? string.Empty).Trim();
                var pendiente = await SolicitudesPendientes.DeUbicacionAsync(_context, ubicacion.Id);
                if (motivo.Length == 0 || pendiente != null)
                {
                    TempData["ErrorMessage"] = motivo.Length == 0
                        ? "Debe indicar el motivo de la eliminación."
                        : SolicitudesPendientes.Mensaje(pendiente!, $"la ubicación '{ubicacion.CodigoUbicacion}'");
                    return RedirectToAction(nameof(Detalle), new { id = ubicacion.BodegaId });
                }

                var solicitud = Solicitud.Nueva(TiposSolicitud.EliminacionUbicacion, userId, new SolicitudEliminacionUbicacion
                {
                    UbicacionId = ubicacion.Id,
                    CodigoUbicacion = ubicacion.CodigoUbicacion,
                    BodegaCodigo = ubicacion.Bodega?.Codigo,
                    Producto = ubicacion.Producto != null ? $"{ubicacion.Producto.Codigo} · {ubicacion.Producto.Nombre}" : "Sin producto",
                    Motivo = motivo
                });

                _context.Solicitudes.Add(solicitud);
                _context.Notificaciones.Add(new Notificacion
                {
                    RolDestinoId = Roles.IdAdministrador,
                    Titulo = "Solicitud de Eliminación de Ubicación",
                    Mensaje = $"El {User.DescripcionParaNotificacion()} solicitó eliminar la ubicación {ubicacion.CodigoUbicacion}.",
                    Tipo = TiposNotificacion.Warning,
                    FechaCreacion = DateTime.Now,
                    Enlace = "/Solicitudes/Index"
                });

                await _context.SaveChangesAsync();

                await _auditService.RegistrarAsync("Solicitud Eliminación Ubicación", "Solicitudes", 
                    $"Supervisor solicitó eliminación de ubicación {ubicacion.CodigoUbicacion}. Motivo: {motivo}");

                TempData["SuccessMessage"] = $"Se ha generado la solicitud de eliminación para la ubicación '{ubicacion.CodigoUbicacion}'. Requiere aprobación del Administrador.";
            }
            else
            {
                TempData["ErrorMessage"] = "No tienes permisos para realizar o solicitar esta acción.";
            }

            return RedirectToAction(nameof(Detalle), new { id = ubicacion.BodegaId });
        }

        // Productos y categorías para el bloque "Producto" del formulario de ubicación
        private async Task PrepararSeleccionProductoAsync(SeleccionProductoViewModel seleccion)
        {
            seleccion.Productos = await _context.Productos
                .OrderBy(p => p.Codigo)
                .Select(p => new ProductoOpcion
                {
                    Id = p.Id,
                    Codigo = p.Codigo,
                    Nombre = p.Nombre,
                    Categoria = p.Categoria != null ? p.Categoria.Nombre : null,
                    UnidadesPorCaja = p.UnidadesPorCaja,
                    Ubicaciones = p.Ubicaciones.Count
                })
                .ToListAsync();
            seleccion.Categorias = await _context.CategoriasProducto.OrderBy(c => c.MinutosPorUnidad).ToListAsync();
            seleccion.NombreCategoriaPredeterminada = seleccion.Categorias.FirstOrDefault(c => c.EsPredeterminada)?.Nombre ?? "por defecto";

            // Sin productos registrados no hay nada que elegir: el formulario empieza en "Nuevo"
            if (seleccion.Productos.Count == 0) seleccion.Modo = SeleccionProductoViewModel.ModoNuevo;

            ViewBag.SeleccionProducto = seleccion;
        }

        // Producto que tendrá la ubicación: uno existente elegido de la lista, o uno nuevo cuyo código no puede
        // repetirse. Un producto existente nunca se modifica aquí: su nombre y datos se editan en Productos.
        // Devuelve null y deja el motivo en ModelState si los datos no son válidos. El producto nuevo no se agrega al contexto.
        private async Task<Producto?> ResolverProductoAsync(SeleccionProductoViewModel seleccion)
        {
            if (seleccion.Modo != SeleccionProductoViewModel.ModoNuevo)
            {
                var existente = seleccion.ProductoId is int productoId ? await _context.Productos.FindAsync(productoId) : null;
                if (existente == null)
                {
                    ModelState.AddModelError(string.Empty, "Selecciona el producto que se guardará en esta ubicación.");
                }
                return existente;
            }

            var numero = (seleccion.NumeroProducto ?? string.Empty).Trim();
            var nombre = (seleccion.NombreProducto ?? string.Empty).Trim();
            var valido = true;

            if (!System.Text.RegularExpressions.Regex.IsMatch(numero, @"^\d+$") || !int.TryParse(numero, out var numeroValor))
            {
                ModelState.AddModelError(string.Empty, "El número de producto es obligatorio y solo debe contener números (máximo 10 dígitos).");
                numeroValor = 0;
                valido = false;
            }
            if (nombre.Length == 0 || nombre.Length > 150)
            {
                ModelState.AddModelError(string.Empty, "El nombre de producto es obligatorio y admite hasta 150 caracteres.");
                valido = false;
            }
            if (!int.TryParse(seleccion.UnidadesPorCaja, out var unidades) || unidades < 1 || unidades > Producto.MaxUnidadesPorCaja)
            {
                ModelState.AddModelError(string.Empty, $"La cantidad máxima por caja debe ser un número entero entre 1 y {Producto.MaxUnidadesPorCaja}.");
                valido = false;
            }
            if (!valido) return null;

            // El código siempre se genera a partir del número: PROD-XXX
            var codigo = $"PROD-{numeroValor:D3}";
            var nombreExistente = await _context.Productos
                .Where(p => p.Codigo.ToUpper() == codigo)
                .Select(p => p.Nombre)
                .FirstOrDefaultAsync();
            if (nombreExistente != null)
            {
                ModelState.AddModelError(string.Empty, $"El producto {codigo} ya existe ({nombreExistente}). Elige «Producto existente» para guardarlo en esta ubicación.");
                return null;
            }

            return new Producto
            {
                Codigo = codigo,
                Nombre = nombre,
                CategoriaProductoId = await ResolverCategoriaAsync(seleccion.CategoriaProductoId),
                UnidadesPorCaja = unidades
            };
        }

        // Valor del formulario → Id de categoría existente, o null (sin clasificar)
        private async Task<int?> ResolverCategoriaAsync(string? valor)
        {
            if (!int.TryParse(valor, out var id)) return null;
            return await _context.CategoriasProducto.AnyAsync(c => c.Id == id) ? id : null;
        }
    }
}
