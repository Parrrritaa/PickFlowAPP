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
        public async Task<IActionResult> CargarExcel(IFormFile archivoExcel)
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

            // ExcelService lee el archivo y valida cada fila contra las bodegas y ubicaciones registradas
            using var stream = archivoExcel.OpenReadStream();
            var validacion = await _excelService.ValidarArchivoExcelAsync(stream);

            if (!validacion.IsValid)
            {
                ViewBag.Errores = validacion.Errors;
                TempData["ErrorMessage"] = "El archivo contiene errores de validación. Revise la lista de observaciones y corríjalas antes de reintentar.";
                return View();
            }

            // Registrar pedidos válidos en base de datos (Paso 8 del CU-23)

            // Los códigos de pedido duplicados ya se rechazaron en ValidarArchivoExcelAsync: un código
            // repetido no se renombra ni se reabre, porque duplicaría el trabajo de alistamiento.
            // AddRange marca todos los pedidos (con sus Detalles) como nuevos y SaveChangesAsync
            // hace todos los INSERT en una sola transacción: se guardan todos o ninguno
            _context.Pedidos.AddRange(validacion.PedidosValidados);

            await _context.SaveChangesAsync();

            await _auditService.RegistrarAsync("Carga Excel Pedidos", "Pedidos", 
                $"Se procesó el archivo '{archivoExcel.FileName}' registrando {validacion.TotalPedidos} pedidos ({validacion.TotalFilas} líneas de productos).");

            TempData["SuccessMessage"] = $"El archivo fue procesado correctamente y los {validacion.TotalPedidos} pedidos quedaron registrados en el sistema.";
            return RedirectToAction(nameof(Index));
        }

        // CU-23: validar el archivo en el servidor sin registrar nada (resultado de validación antes de confirmar la carga)
        [Authorize(Roles = Roles.AdministradorOSupervisor)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ValidarExcel(IFormFile archivoExcel)
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
            var v = await _excelService.ValidarArchivoExcelAsync(stream);

            // Responde en JSON: el JavaScript de CargarExcel.cshtml lo lee y pinta el resultado sin recargar la página
            return Json(new
            {
                valido = v.IsValid,
                tipoValido = v.ArchivoLegible,
                estructuraValida = v.EstructuraValida,
                filasConCantidadInvalida = v.FilasConCantidadInvalida,
                filasConBodegaNoRegistrada = v.FilasConBodegaNoRegistrada,
                filasConUbicacionNoRegistrada = v.FilasConUbicacionNoRegistrada,
                filasConProductoIncorrecto = v.FilasConProductoIncorrecto,
                filasConPedidoDuplicado = v.FilasConPedidoDuplicado,
                totalPedidos = v.TotalPedidos,
                totalFilas = v.TotalFilas,
                errores = v.Errors
            });
        }
    }
}
