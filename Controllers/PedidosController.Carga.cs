using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickingApp.Models;
using PickingApp.Services;

namespace PickingApp.Controllers
{
    // Carga de pedidos desde Excel (CU-23). Parte de PedidosController: mismas rutas (/Pedidos/...) y dependencias.
    public partial class PedidosController
    {
        // CU-23: Cargar archivo Excel de pedidos
        [Authorize(Roles = Roles.AdministradorOSupervisor)]
        [HttpGet]
        public IActionResult CargarExcel()
        {
            return View();
        }

        [Authorize(Roles = Roles.AdministradorOSupervisor)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CargarExcel(IFormFile archivoExcel, List<ResolucionExcel>? resoluciones)
        {
            if (archivoExcel == null || archivoExcel.Length == 0)
            {
                TempData["ErrorMessage"] = "No fue posible leer el archivo seleccionado. Verifica que el formato sea correcto e inténtalo nuevamente.";
                return View();
            }

            if (!archivoExcel.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            {
                TempData["ErrorMessage"] = "El archivo no cumple con la estructura esperada. Debe ser un archivo de Excel (.xlsx). Verifica el formato e inténtalo nuevamente.";
                return View();
            }

            // ExcelService lee el archivo, valida cada fila contra las bodegas y ubicaciones registradas
            // y aplica las decisiones del usuario sobre productos y ubicaciones (resoluciones)
            using var stream = archivoExcel.OpenReadStream();
            var validacion = await _excelService.ValidarArchivoExcelAsync(stream, resoluciones ?? new(), User.IsInRole(Roles.Administrador));

            if (!validacion.IsValid)
            {
                // Pasa si algo cambió en el sistema entre la validación previa y la confirmación
                ViewBag.Errores = validacion.Errors
                    .Concat(validacion.ErroresResolucion.Values)
                    .DefaultIfEmpty("Hay filas con productos o ubicaciones sin resolver. Valide el archivo de nuevo.")
                    .ToList();
                TempData["ErrorMessage"] = "El archivo contiene errores de validación. Revise la lista de observaciones y corríjalas antes de reintentar.";
                return View();
            }

            // Registrar pedidos válidos en base de datos (Paso 8 del CU-23)

            // Los códigos de pedido duplicados ya se rechazaron en ValidarArchivoExcelAsync: un código
            // repetido no se renombra ni se reabre, porque duplicaría el trabajo de alistamiento.
            // AddRange marca todos los pedidos (con sus Detalles) como nuevos y SaveChangesAsync
            // hace todos los INSERT en una sola transacción: se guardan todos o ninguno.
            // Los productos y ubicaciones nuevos van enlazados a los detalles y se insertan en la misma transacción;
            // las ubicaciones ajustadas (producto asignado o activadas) se actualizan también ahí.
            _context.Pedidos.AddRange(validacion.PedidosValidados);

            await _context.SaveChangesAsync();

            var ajustes = validacion.Cambios.Any() ? $" Ajustes: {string.Join("; ", validacion.Cambios)}." : string.Empty;
            await _auditService.RegistrarAsync("Carga Excel Pedidos", "Pedidos",
                $"Se procesó el archivo '{archivoExcel.FileName}' registrando {validacion.TotalPedidos} pedidos ({validacion.TotalFilas} líneas de productos).{ajustes}");

            TempData["SuccessMessage"] = $"El archivo fue procesado correctamente y los {validacion.TotalPedidos} pedidos quedaron registrados en el sistema."
                + (validacion.Cambios.Any() ? $" También se aplicaron {validacion.Cambios.Count} cambios de productos y ubicaciones (ver Auditoría)." : string.Empty);
            return RedirectToAction(nameof(Index));
        }

        // CU-23: validar el archivo en el servidor sin registrar nada (resultado de validación antes de confirmar la carga).
        // También revisa las decisiones sobre las incidencias antes de confirmar (resoluciones), sin guardarlas.
        [Authorize(Roles = Roles.AdministradorOSupervisor)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ValidarExcel(IFormFile archivoExcel, List<ResolucionExcel>? resoluciones)
        {
            var esXlsx = archivoExcel != null && archivoExcel.Length > 0
                && archivoExcel.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase);
            if (!esXlsx)
            {
                return Json(new
                {
                    valido = false,
                    tipoValido = false,
                    errores = new[] { "El archivo debe ser un Excel (.xlsx) con contenido." }
                });
            }

            using var stream = archivoExcel!.OpenReadStream();
            var v = await _excelService.ValidarArchivoExcelAsync(stream, resoluciones ?? new(), User.IsInRole(Roles.Administrador));

            // Bodegas y categorías para los formularios de las incidencias (ubicación nueva y producto nuevo)
            var bodegas = await _context.Bodegas.Where(b => b.Activa).OrderBy(b => b.Codigo)
                .Select(b => new { id = b.Id, codigo = b.Codigo, nombre = b.Nombre })
                .ToListAsync();
            var categorias = await _context.CategoriasProducto.OrderBy(c => c.MinutosPorUnidad).ToListAsync();

            // Responde en JSON: el JavaScript de CargarExcel.cshtml lo lee y pinta el resultado sin recargar la página
            return Json(new
            {
                valido = v.IsValid,
                tipoValido = v.ArchivoLegible,
                estructuraValida = v.EstructuraValida,
                filasConCantidadInvalida = v.FilasConCantidadInvalida,
                filasConBodegaNoRegistrada = v.FilasConBodegaNoRegistrada,
                filasConProductoNoRegistrado = v.FilasConProductoNoRegistrado,
                filasConUbicacionNoRegistrada = v.FilasConUbicacionNoRegistrada,
                filasConProductoIncorrecto = v.FilasConProductoIncorrecto,
                filasConNombreDistinto = v.FilasConNombreDistinto,
                filasConPedidoDuplicado = v.FilasConPedidoDuplicado,
                totalPedidos = v.TotalPedidos,
                totalFilas = v.TotalFilas,
                errores = v.Errors,
                incidencias = v.Incidencias,
                erroresResolucion = v.ErroresResolucion,
                bodegas,
                categorias = categorias.Select(c => new { id = c.Id, nombre = c.Nombre, minutos = c.MinutosPorUnidad.ToString("0.##") }),
                categoriaPredeterminada = categorias.FirstOrDefault(c => c.EsPredeterminada)?.Nombre ?? "por defecto"
            });
        }
    }
}
