using System.Security.Claims;
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
        public async Task<IActionResult> RegistrarNovedad(int pedidoId, string tipoNovedad, string descripcion)
        {
            if (string.IsNullOrWhiteSpace(descripcion))
            {
                TempData["ErrorMessage"] = "La descripción de la novedad es obligatoria.";
                return RedirectToAction(nameof(Detalle), new { id = pedidoId });
            }

            var pedido = await _context.Pedidos.FindAsync(pedidoId);
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

            _context.NovedadesPedido.Add(novedad);

            _context.Notificaciones.Add(new Notificacion
            {
                RolDestino = Roles.Administrador,
                Titulo = "Novedad en Pedido",
                Mensaje = $"Se registró una novedad de tipo '{tipoNovedad}' en el pedido {pedido.CodigoPedido}.",
                Tipo = "Warning",
                FechaCreacion = DateTime.Now,
                Enlace = $"/Pedidos/Detalle/{pedidoId}"
            });

            await _context.SaveChangesAsync();

            await _auditService.RegistrarAsync("Registrar Novedad", "Pedidos", 
                $"Novedad registrada en pedido {pedido.CodigoPedido}: [{tipoNovedad}] {descripcion}");

            TempData["SuccessMessage"] = "Novedad registrada correctamente en el pedido.";
            return RedirectToAction(nameof(Detalle), new { id = pedidoId });
        }

        // CU-30: Atender novedad (Solo Administrador)
        [Authorize(Roles = Roles.Administrador)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AtenderNovedad(int novedadId, string observacionesAtencion)
        {
            var novedad = await _context.NovedadesPedido
                .Include(n => n.Pedido)
                .FirstOrDefaultAsync(n => n.Id == novedadId);

            if (novedad == null) return NotFound();

            var userName = User.Identity?.Name ?? "Usuario";

            novedad.Atendida = true;
            novedad.AtendidaPor = userName;
            novedad.FechaAtencion = DateTime.Now;
            novedad.ObservacionesAtencion = observacionesAtencion?.Trim() ?? "Atendida y solventada";

            await _context.SaveChangesAsync();

            await _auditService.RegistrarAsync("Atender Novedad", "Pedidos", 
                $"Novedad #{novedad.Id} del pedido {novedad.Pedido?.CodigoPedido} marcada como atendida.");

            TempData["SuccessMessage"] = "La novedad ha sido marcada como atendida.";
            return RedirectToAction(nameof(Detalle), new { id = novedad.PedidoId });
        }
    }
}
