using System.Security.Claims;
using PickingApp.Models;

namespace PickingApp.Services
{
    // Filtro central por bodega: el Administrador ve todas; Supervisor y Auxiliar solo la suya. Toda consulta
    // de listas o por id sobre estas tablas pasa por aquí, y lo que queda fuera responde 404 (pantalla Home/Estado).
    public static class AlcanceBodega
    {
        // Null = sin filtro (Administrador). Un Supervisor o Auxiliar sin bodega en la cookie no debería existir
        // (SesionUsuario lo saca), pero si pasara recibe -1, que no coincide con ninguna bodega: nunca ve todo.
        public static int? BodegaVisible(this ClaimsPrincipal user) =>
            user.IsInRole(Roles.Administrador) ? null : user.IdBodega() ?? -1;

        public static IQueryable<Bodega> DelAlcance(this IQueryable<Bodega> q, ClaimsPrincipal user) =>
            user.BodegaVisible() is int b ? q.Where(x => x.Id == b) : q;

        public static IQueryable<Pedido> DelAlcance(this IQueryable<Pedido> q, ClaimsPrincipal user) =>
            user.BodegaVisible() is int b ? q.Where(x => x.BodegaId == b) : q;

        public static IQueryable<Ubicacion> DelAlcance(this IQueryable<Ubicacion> q, ClaimsPrincipal user) =>
            user.BodegaVisible() is int b ? q.Where(x => x.BodegaId == b) : q;

        public static IQueryable<Empleado> DelAlcance(this IQueryable<Empleado> q, ClaimsPrincipal user) =>
            user.BodegaVisible() is int b ? q.Where(x => x.BodegaId == b) : q;

        public static IQueryable<NovedadPedido> DelAlcance(this IQueryable<NovedadPedido> q, ClaimsPrincipal user) =>
            user.BodegaVisible() is int b ? q.Where(x => x.Pedido!.BodegaId == b) : q;

        public static IQueryable<Solicitud> DelAlcance(this IQueryable<Solicitud> q, ClaimsPrincipal user) =>
            user.BodegaVisible() is int b ? q.Where(x => x.BodegaId == b) : q;
    }
}
