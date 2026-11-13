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
        [Authorize(Roles = Roles.AdministradorSupervisorOAuxiliar)]
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
                RegistradoPorId = User.IdUsuario(),
                Atendida = false
            };

            // Se agregan la novedad y los avisos a quienes pueden atenderla; todo se guarda en el mismo SaveChangesAsync
            _context.NovedadesPedido.Add(novedad);

            var titulo = "Novedad en Pedido";
            var mensaje = $"Se registró una novedad de tipo '{tipoNovedad}' en el pedido {pedido.CodigoPedido}.";
            var enlace = $"/Pedidos/Detalle/{pedidoId}";
            await Avisos.AlRolAsync(_context, Roles.IdAdministrador, titulo, mensaje, TiposNotificacion.Warning, enlace);

            // Supervisores de la bodega del pedido, salvo quien la registró
            var supervisores = await _context.Usuarios
                .Where(u => u.RolId == Roles.IdSupervisor && u.BodegaId == pedido.BodegaId && u.Id != novedad.RegistradoPorId)
                .Select(u => u.Id).ToListAsync();
            foreach (var id in supervisores)
                Avisos.AlUsuario(_context, id, titulo, mensaje, TiposNotificacion.Warning, enlace);

            await _context.SaveChangesAsync();

            await _auditService.RegistrarAsync(AccionesLog.RegistrarNovedad, "Pedidos", 
                $"Novedad registrada en pedido {pedido.CodigoPedido}: [{tipoNovedad}] {descripcion}");

            TempData["SuccessMessage"] = "Novedad registrada correctamente en el pedido.";
            return RedirectToAction(nameof(Detalle), new { id = pedidoId });
        }

        // CU-30: Atender novedad (Administrador, o Supervisor en su bodega: DelAlcance filtra la novedad)
        [Authorize(Roles = Roles.AdministradorOSupervisor)]
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

            // Quien la registró se entera de cómo se resolvió (se guarda junto con la atención)
            if (novedad.RegistradoPorId is int registrante && registrante != User.IdUsuario())
            {
                Avisos.AlUsuario(_context, registrante, "Novedad Atendida",
                    $"La novedad '{novedad.TipoNovedad}' del pedido {novedad.Pedido?.CodigoPedido} fue atendida por el {User.DescripcionParaNotificacion()}. " +
                    $"Observación: {novedad.ObservacionesAtencion}",
                    TiposNotificacion.Success, $"/Pedidos/Detalle/{novedad.PedidoId}#novedades");
            }

            await _context.SaveChangesAsync();

            await _auditService.RegistrarAsync(AccionesLog.AtenderNovedad, "Pedidos", 
                $"Novedad #{novedad.Id} del pedido {novedad.Pedido?.CodigoPedido} marcada como atendida. Observación: {novedad.ObservacionesAtencion}");

            TempData["SuccessMessage"] = "La novedad ha sido marcada como atendida.";
            return RedirectToAction(nameof(Detalle), new { id = novedad.PedidoId });
        }
    }
}
