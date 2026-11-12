using Microsoft.EntityFrameworkCore;
using PickingApp.Data;
using PickingApp.Models;

namespace PickingApp.Services
{
    // Desde: null si no se conoce el inicio; Hasta: null si sigue allí
    public record PeriodoBodega(string Bodega, DateTime? Desde, DateTime? Hasta);

    // Todo cambio de bodega pasa por aquí para que quede registrado. Los pedidos no se tocan: siguen en su bodega.
    public static class Traslados
    {
        public static async Task CambiarBodegaAsync(ApplicationDbContext context, Empleado empleado, int? destino)
        {
            if (empleado.BodegaId is int origen && destino is int nueva && origen != nueva)
            {
                // Regresa a una bodega que ya dejó: vuelve activo y allí siguen sus pedidos de antes
                if (await context.TrasladosPersonal.AnyAsync(t => t.EmpleadoId == empleado.Id && t.BodegaOrigenId == nueva))
                {
                    empleado.Activo = true;
                }
                context.TrasladosPersonal.Add(new TrasladoPersonal { Empleado = empleado, BodegaOrigenId = origen, BodegaDestinoId = nueva });
            }
            empleado.BodegaId = destino;
        }

        public static void CambiarBodega(ApplicationDbContext context, Usuario usuario, int? destino)
        {
            if (usuario.BodegaId is int origen && destino is int nueva && origen != nueva)
            {
                context.TrasladosPersonal.Add(new TrasladoPersonal { Usuario = usuario, BodegaOrigenId = origen, BodegaDestinoId = nueva });
            }
            usuario.BodegaId = destino;
        }

        public static Task<List<TrasladoPersonal>> DeEmpleadoAsync(ApplicationDbContext context, int empleadoId) =>
            Ordenados(context).Where(t => t.EmpleadoId == empleadoId).ToListAsync();

        public static Task<List<TrasladoPersonal>> DeUsuarioAsync(ApplicationDbContext context, int usuarioId) =>
            Ordenados(context).Where(t => t.UsuarioId == usuarioId).ToListAsync();

        private static IQueryable<TrasladoPersonal> Ordenados(ApplicationDbContext context) => context.TrasladosPersonal
            .Include(t => t.BodegaOrigen)
            .Include(t => t.BodegaDestino)
            .OrderBy(t => t.Fecha).ThenBy(t => t.Id);

        // Tramos en cada bodega. Sin traslados no hay tramos: la persona siempre ha estado en la misma.
        public static List<PeriodoBodega> Periodos(List<TrasladoPersonal> traslados, DateTime? inicio)
        {
            var periodos = new List<PeriodoBodega>();
            var desde = inicio;
            foreach (var t in traslados)
            {
                periodos.Add(new PeriodoBodega(t.BodegaOrigen!.Nombre, desde, t.Fecha));
                desde = t.Fecha;
            }
            if (traslados.Any()) periodos.Add(new PeriodoBodega(traslados[^1].BodegaDestino!.Nombre, desde, null));
            return periodos;
        }

        // Quienes dejaron la bodega y hoy no están en ella, con su salida más reciente de allí
        public static async Task<List<TrasladoPersonal>> EmpleadosQueSalieronAsync(ApplicationDbContext context, int bodegaId) =>
            (await context.TrasladosPersonal
                .Include(t => t.Empleado).Include(t => t.BodegaDestino)
                .Where(t => t.BodegaOrigenId == bodegaId && t.EmpleadoId != null && t.Empleado!.BodegaId != bodegaId)
                .OrderByDescending(t => t.Fecha).ThenByDescending(t => t.Id)
                .ToListAsync())
            .DistinctBy(t => t.EmpleadoId).OrderBy(t => t.Empleado!.NombreCompleto).ToList();

        public static async Task<List<TrasladoPersonal>> AuxiliaresQueSalieronAsync(ApplicationDbContext context, int bodegaId) =>
            (await context.TrasladosPersonal
                .Include(t => t.Usuario).Include(t => t.BodegaDestino)
                .Where(t => t.BodegaOrigenId == bodegaId && t.UsuarioId != null && t.Usuario!.RolId == Roles.IdAuxiliar && t.Usuario.BodegaId != bodegaId)
                .OrderByDescending(t => t.Fecha).ThenByDescending(t => t.Id)
                .ToListAsync())
            .DistinctBy(t => t.UsuarioId).OrderBy(t => t.Usuario!.NombreCompleto).ToList();
    }
}
