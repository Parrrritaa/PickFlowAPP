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
        // Un Excel de pedidos pesa unos pocos KB: 5 MB deja margen de sobra y evita que un archivo enorme
        // (o un .xlsx comprimido que se infla al abrirlo) consuma la memoria del servidor.
        private const long MaxBytesExcel = 5 * 1024 * 1024;
        private const string MensajeExcelMuyGrande = "El archivo pesa más de 5 MB. Un Excel de pedidos normalmente pesa unos pocos KB: revisa que sea el archivo correcto.";

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

            if (archivoExcel.Length > MaxBytesExcel)
            {
                TempData["ErrorMessage"] = MensajeExcelMuyGrande;
                return View();
            }

            // ExcelService lee el archivo, valida cada fila contra las bodegas y productos registrados, elige la
            // ubicación de cada producto y aplica las confirmaciones de nombre (resoluciones).
            // El Supervisor solo carga pedidos de su bodega (BodegaVisible: null para el Administrador).
            using var stream = archivoExcel.OpenReadStream();
            var validacion = await _excelService.ValidarArchivoExcelAsync(stream, resoluciones ?? new(), User.BodegaVisible());

            if (!validacion.IsValid)
            {
                // Pasa si algo cambió en el sistema entre la validación previa y la confirmación.
                // El modelo de la vista es la lista de observaciones (sin errores, la vista recibe null)
                TempData["ErrorMessage"] = "El archivo contiene errores de validación. Revise la lista de observaciones y corríjalas antes de reintentar.";
                return View(validacion.Errors
                    .Concat(validacion.ErroresResolucion.Values)
                    .DefaultIfEmpty("Hay nombres de productos sin confirmar. Valide el archivo de nuevo.")
                    .ToList());
            }

            // Registrar pedidos válidos en base de datos (Paso 8 del CU-23)

            // Los códigos de pedido duplicados ya se rechazaron en ValidarArchivoExcelAsync: un código
            // repetido no se renombra ni se reabre, porque duplicaría el trabajo de alistamiento.
            // AddRange marca todos los pedidos (con sus Detalles) como nuevos y SaveChangesAsync
            // hace todos los INSERT en una sola transacción: se guardan todos o ninguno.
            // Las novedades de productos no disponibles van en los pedidos y se insertan en la misma transacción
            var noDisponibles = validacion.PedidosValidados.SelectMany(p => p.Novedades).ToList();
            noDisponibles.ForEach(n => n.RegistradoPor = User.Identity?.Name ?? "Usuario");
            _context.Pedidos.AddRange(validacion.PedidosValidados);

            await _context.SaveChangesAsync();

            var avisos = noDisponibles.Any() ? $" {noDisponibles.Count} productos no disponibles en la bodega de su pedido quedaron como novedad." : string.Empty;
            await _auditService.RegistrarAsync(AccionesLog.CargaExcelPedidos, "Pedidos",
                $"Se procesó el archivo '{archivoExcel.FileName}' registrando {validacion.TotalPedidos} pedidos ({validacion.TotalFilas} líneas de productos).{avisos}");

            TempData["SuccessMessage"] = $"El archivo fue procesado correctamente y los {validacion.TotalPedidos} pedidos quedaron registrados en el sistema."
                + (noDisponibles.Any() ? $" {noDisponibles.Count} productos no estaban disponibles en su bodega: quedaron como novedad «No disponible» en sus pedidos." : string.Empty);
            return RedirectToAction(nameof(Index));
        }

        // CU-23: validar el archivo en el servidor sin registrar nada (resultado de validación antes de confirmar la carga).
        // También revisa las confirmaciones de nombre antes de confirmar (resoluciones), sin guardarlas.
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

            if (archivoExcel!.Length > MaxBytesExcel)
            {
                return Json(new { valido = false, tipoValido = false, errores = new[] { MensajeExcelMuyGrande } });
            }

            using var stream = archivoExcel!.OpenReadStream();
            var v = await _excelService.ValidarArchivoExcelAsync(stream, resoluciones ?? new(), User.BodegaVisible());

            // Responde en JSON: el JavaScript de CargarExcel.cshtml lo lee y pinta el resultado sin recargar la página
            return Json(new
            {
                valido = v.IsValid,
                tipoValido = v.ArchivoLegible,
                estructuraValida = v.EstructuraValida,
                filasConCantidadInvalida = v.FilasConCantidadInvalida,
                filasConBodegaNoRegistrada = v.FilasConBodegaNoRegistrada,
                // Solo al Supervisor se le muestra la verificación "Pedidos de su bodega"
                soloSuBodega = User.BodegaVisible() != null,
                filasDeOtraBodega = v.FilasDeOtraBodega,
                filasConProductoNoRegistrado = v.FilasConProductoNoRegistrado,
                filasConNombreDistinto = v.FilasConNombreDistinto,
                filasConPedidoDuplicado = v.FilasConPedidoDuplicado,
                filasNoDisponibles = v.FilasNoDisponibles,
                noDisponibles = v.NoDisponibles,
                totalPedidos = v.TotalPedidos,
                totalFilas = v.TotalFilas,
                errores = v.Errors,
                incidencias = v.Incidencias,
                erroresResolucion = v.ErroresResolucion
            });
        }
    }
}
