using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickingApp.Models;
using PickingApp.Models.ViewModels;
using PickingApp.Services;

namespace PickingApp.Controllers
{
    // División de pedidos grandes en partes. Parte de PedidosController: mismas rutas (/Pedidos/...) y dependencias.
    public partial class PedidosController
    {
        // Dividir un pedido grande en partes que se asignan por separado (p. ej. cuando supera una jornada)
        [Authorize(Roles = Roles.AdministradorOSupervisor)]
        [HttpGet]
        public async Task<IActionResult> Dividir(int id)
        {
            var pedido = await _context.Pedidos.DelAlcance(User)
                .Include(p => p.Bodega)
                .Include(p => p.Detalles)
                    .ThenInclude(d => d.Ubicacion)
                        .ThenInclude(u => u!.Producto)
                            .ThenInclude(pr => pr!.Categoria)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (pedido == null) return NotFound();

            var error = pedido.MotivoNoDivisible();
            if (error != null)
            {
                TempData["ErrorMessage"] = error;
                return RedirectToAction(nameof(Detalle), new { id });
            }

            var tiempoEstandar = await _pickingEngine.CalcularTiempoEstandarAsync(id);
            var jornadaMinutos = await ParametrosSistema.ObtenerDecimalAsync(_context, ParametrosSistema.MaxHorasJornada) * 60;
            return View(new DividirPedidoViewModel
            {
                Pedido = pedido,
                TiempoEstandar = tiempoEstandar,
                JornadaMinutos = jornadaMinutos,
                MinutosPorLinea = await _pickingEngine.CalcularMinutosPorLineaAsync(id),
                MinutosPorUbicacion = await ParametrosSistema.ObtenerDecimalAsync(_context, ParametrosSistema.MinutosPorUbicacion),
                MaxPartes = pedido.MaxPartesPermitidas,
                PartesSugeridas = pedido.PartesSugeridas(tiempoEstandar, jornadaMinutos)
            });
        }

        // cantidades["{detalleId}_{parte}"] = unidades de esa línea que van en esa parte
        [Authorize(Roles = Roles.AdministradorOSupervisor)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Dividir(int pedidoId, int partes, Dictionary<string, string>? cantidades)
        {
            var pedido = await _context.Pedidos.DelAlcance(User)
                .Include(p => p.Detalles)
                .FirstOrDefaultAsync(p => p.Id == pedidoId);

            if (pedido == null) return NotFound();

            var error = pedido.MotivoNoDivisible();
            if (error != null)
            {
                TempData["ErrorMessage"] = error;
                return RedirectToAction(nameof(Detalle), new { id = pedidoId });
            }

            var maxPartes = pedido.MaxPartesPermitidas;
            if (partes < 2 || partes > maxPartes)
            {
                return ErrorDivision(pedidoId, $"El número de partes debe estar entre 2 y {maxPartes}.");
            }

            cantidades ??= new Dictionary<string, string>();
            var detalles = pedido.Detalles.OrderBy(d => d.Id).ToList();
            var reparto = new Dictionary<(int DetalleId, int Parte), int>();

            // Valida que cada producto se reparta completo: la suma de sus unidades en todas las partes
            // debe ser igual a la cantidad original de la línea
            foreach (var d in detalles)
            {
                var suma = 0;
                for (var k = 1; k <= partes; k++)
                {
                    var texto = cantidades.GetValueOrDefault($"{d.Id}_{k}");
                    var unidades = 0;
                    if (!string.IsNullOrWhiteSpace(texto) && (!int.TryParse(texto, out unidades) || unidades < 0))
                    {
                        return ErrorDivision(pedidoId, $"La cantidad de «{d.NombreProducto}» en la parte {k} no es válida.");
                    }
                    reparto[(d.Id, k)] = unidades;
                    suma += unidades;
                }
                if (suma != d.Cantidad)
                {
                    return ErrorDivision(pedidoId, $"«{d.NombreProducto}» tiene {d.Cantidad} unidades y se repartieron {suma}.");
                }
            }

            for (var k = 1; k <= partes; k++)
            {
                if (detalles.All(d => reparto[(d.Id, k)] == 0))
                {
                    return ErrorDivision(pedidoId, $"La parte {k} quedó sin productos.");
                }
            }

            var codigos = Enumerable.Range(1, partes).Select(k => $"{pedido.CodigoPedido}-P{k}").ToList();
            if (codigos.Any(c => c.Length > 50) || await _context.Pedidos.AnyAsync(p => p.BodegaId == pedido.BodegaId && codigos.Contains(p.CodigoPedido)))
            {
                return ErrorDivision(pedidoId, "Ya existen pedidos con los códigos de las partes; revise los pedidos cargados.");
            }

            // Cada parte es un pedido nuevo (PED-XXX-P1, P2...) que apunta al original con PedidoPadreId
            var resumen = new List<string>();
            for (var k = 1; k <= partes; k++)
            {
                var parte = pedido.NuevaParte(k);
                parte.Detalles = detalles.Where(d => reparto[(d.Id, k)] > 0).Select(d => d.Copia(reparto[(d.Id, k)])).ToList();
                _context.Pedidos.Add(parte);
                resumen.Add($"{parte.CodigoPedido} ({parte.Detalles.Sum(d => d.Cantidad)} uds)");
            }

            // Un solo SaveChanges: inserta las partes y actualiza el original a "Dividido" en la misma transacción
            pedido.Estado = PedidoEstados.Dividido;
            await _context.SaveChangesAsync();

            await _auditService.RegistrarAsync(AccionesLog.DividirPedido, "Pedidos",
                $"Pedido {pedido.CodigoPedido} dividido en {partes} partes: {string.Join(", ", resumen)}.");

            TempData["SuccessMessage"] = $"Pedido '{pedido.CodigoPedido}' dividido en {partes} partes. Ahora puede asignar cada parte.";
            return RedirectToAction(nameof(Detalle), new { id = pedidoId });
        }

        // Volver a unir las partes mientras ninguna se haya asignado ni tenga novedades
        [Authorize(Roles = Roles.AdministradorOSupervisor)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeshacerDivision(int pedidoId)
        {
            var pedido = await _context.Pedidos.DelAlcance(User)
                .Include(p => p.Partes)
                    .ThenInclude(pa => pa.Novedades)
                .FirstOrDefaultAsync(p => p.Id == pedidoId);

            if (pedido == null) return NotFound();

            if (pedido.Estado != PedidoEstados.Dividido)
            {
                TempData["ErrorMessage"] = "El pedido no está dividido.";
                return RedirectToAction(nameof(Detalle), new { id = pedidoId });
            }

            if (!pedido.PuedeDeshacerDivision)
            {
                TempData["ErrorMessage"] = "No se puede deshacer la división: alguna parte ya fue asignada, finalizada o tiene novedades.";
                return RedirectToAction(nameof(Detalle), new { id = pedidoId });
            }

            var totalPartes = pedido.Partes.Count;
            // RemoveRange genera los DELETE de las partes; sus detalles se borran en cascada (ApplicationDbContext)
            _context.Pedidos.RemoveRange(pedido.Partes);
            pedido.Estado = PedidoEstados.Pendiente;
            await _context.SaveChangesAsync();

            await _auditService.RegistrarAsync("Deshacer División", "Pedidos",
                $"Se unieron de nuevo las {totalPartes} partes del pedido {pedido.CodigoPedido}.");

            TempData["SuccessMessage"] = $"Se deshizo la división: el pedido '{pedido.CodigoPedido}' vuelve a estar pendiente como un solo pedido.";
            return RedirectToAction(nameof(Detalle), new { id = pedidoId });
        }

        private IActionResult ErrorDivision(int pedidoId, string mensaje)
        {
            TempData["ErrorMessage"] = mensaje;
            return RedirectToAction(nameof(Dividir), new { id = pedidoId });
        }
    }
}
