using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickingApp.Data;

namespace PickingApp.ViewComponents
{
    // Opciones del selector de bodega en los formularios de usuarios y empleados. Solo bodegas activas,
    // más la ya asignada aunque esté inactiva, para que al editar se vea dónde está la persona hoy.
    public class OpcionesBodegaViewComponent : ViewComponent
    {
        private readonly ApplicationDbContext _context;

        public OpcionesBodegaViewComponent(ApplicationDbContext context)
        {
            _context = context;
        }

        // excluida: la bodega propia, al pedir un traslado a otra
        public async Task<IViewComponentResult> InvokeAsync(int? seleccionada, int? excluida = null)
        {
            var bodegas = await _context.Bodegas.AsNoTracking()
                .Where(b => (b.Activa || b.Id == seleccionada) && b.Id != excluida)
                .OrderBy(b => b.Nombre)
                .ToListAsync();
            return View((bodegas, seleccionada));
        }
    }
}
