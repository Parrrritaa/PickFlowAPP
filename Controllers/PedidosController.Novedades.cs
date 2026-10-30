using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickingApp.Models;
using PickingApp.Services;

namespace PickingApp.Controllers
{
    // Novedades del pedido (CU-29, CU-30). Parte de PedidosController: mismas rutas (/Pedidos/...) y dependencias.
    public partial class PedidosController
    {
        // CU-29: Registrar novedad al pedido
        [Authorize(Roles = Roles.AdministradorOSupervisor)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RegistrarNovedad(int pedidoId, string tipoNovedad,
            [Required(ErrorMessage = "La descripción de la novedad es obligatoria.")]
            [StringLength(500, ErrorMessage = "La descripción de la novedad admite hasta {1} caracteres.")] string descripcion)
        {
            if (!TiposNovedad.Todos.Contains(tipoNovedad))
            {
                ModelState.AddModelError(nameof(tipoNovedad), "Seleccione un tipo de novedad válido.");
            }
            if (!ModelState.IsValid)
            {
                TempData["ErrorMessage"] = ModelState.PrimerError();
                return RedirectToAction(nameof(Detalle), new { id = pedidoId });
            }

            var pedido = await _context.Pedidos.DelAlcance(User).FirstOrDefaultAsync(x => x.Id == pedidoId);
            if (pedido == null) return NotFound();

            var userName = User.Identity?.Name ?? "Usuario";

            var novedad = new NovedadPedido
            {
                PedidoId = pedidoId,
                TipoNovedad = tipoNovedad,
                Descripcion = descripcion.Trim(),
                FechaRegistro = DateTime.Now,
                RegistradoPor = userName,
                Atendida = false
            };

            // Se agregan la novedad y la notificación al Administrador; ambas se guardan en el mismo SaveChangesAsync
            _context.NovedadesPedido.Add(novedad);

            await Avisos.AlRolAsync(_context, Roles.IdAdministrador,
                "Novedad en Pedido",
                $"Se registró una novedad de tipo '{tipoNovedad}' en el pedido {pedido.CodigoPedido}.",
                TiposNotificacion.Warning,
                $"/Pedidos/Detalle/{pedidoId}");

            await _context.SaveChangesAsync();

            await _auditService.RegistrarAsync(AccionesLog.RegistrarNovedad, "Pedidos", 
                $"Novedad registrada en pedido {pedido.CodigoPedido}: [{tipoNovedad}] {descripcion}");

            TempData["SuccessMessage"] = "Novedad registrada correctamente en el pedido.";
            return RedirectToAction(nameof(Detalle), new { id = pedidoId });
        }

        // CU-30: Atender novedad (Solo Administrador)
        [Authorize(Roles = Roles.Administrador)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AtenderNovedad(int novedadId,
            [StringLength(500, ErrorMessage = "Las observaciones admiten hasta {1} caracteres.")] string? observacionesAtencion)
        {
            var novedad = await _context.NovedadesPedido.DelAlcance(User)
                .Include(n => n.Pedido)
                .FirstOrDefaultAsync(n => n.Id == novedadId);

            if (novedad == null) return NotFound();

            if (!ModelState.IsValid)
            {
                TempData["ErrorMessage"] = ModelState.PrimerError();
                return RedirectToAction(nameof(Detalle), new { id = novedad.PedidoId });
            }

            var userName = User.Identity?.Name ?? "Usuario";

            novedad.Atendida = true;
            novedad.AtendidaPor = userName;
            novedad.FechaAtencion = DateTime.Now;
            novedad.ObservacionesAtencion = observacionesAtencion?.Trim() ?? "Atendida y solventada";

            await _context.SaveChangesAsync();

            await _auditService.RegistrarAsync(AccionesLog.AtenderNovedad, "Pedidos", 
                $"Novedad #{novedad.Id} del pedido {novedad.Pedido?.CodigoPedido} marcada como atendida.");

            TempData["SuccessMessage"] = "La novedad ha sido marcada como atendida.";
            return RedirectToAction(nameof(Detalle), new { id = novedad.PedidoId });
        }
    }
}
