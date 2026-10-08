using Microsoft.EntityFrameworkCore;
using PickingApp.Data;
using PickingApp.Models;

namespace PickingApp.Services
{
    public static class ReglasUbicacion
    {
        // Una ubicación que aparece en pedidos por recoger no puede cambiar de producto ni desactivarse: el operario
        // iría a buscar un producto que ya no está ahí y el tiempo estándar saldría de la categoría del producto nuevo.
        // Devuelve el motivo del impedimento, o null si el cambio se puede aplicar.
        public static async Task<string?> ValidarCambioAsync(ApplicationDbContext context, Ubicacion actual, bool cambiaProducto, bool quedaActiva)
        {
            var seDesactiva = actual.Activa && !quedaActiva;
            if (!cambiaProducto && !seDesactiva) return null;

            var pedidos = await context.DetallesPedido
                .Where(d => d.UbicacionId == actual.Id && PedidoEstados.SinTerminar.Contains(d.Pedido!.Estado))
                .Select(d => d.Pedido!.CodigoPedido)
                .Distinct()
                .OrderBy(c => c)
                .ToListAsync();
            if (pedidos.Count == 0) return null;

            var accion = cambiaProducto
                ? $"cambiar el producto de la ubicación {actual.CodigoUbicacion}"
                : $"desactivar la ubicación {actual.CodigoUbicacion}";
            var lista = string.Join(", ", pedidos.Take(5)) + (pedidos.Count > 5 ? $" y {pedidos.Count - 5} más" : "");
            return $"No se puede {accion}: está en pedidos sin terminar ({lista}). Complete o cancele esos pedidos primero.";
        }
    }
}
