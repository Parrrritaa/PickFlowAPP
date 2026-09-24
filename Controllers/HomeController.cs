using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickingApp.Data;
using PickingApp.Models;

namespace PickingApp.Controllers
{
    [Authorize]
    public class HomeController : Controller
    {
        private readonly ApplicationDbContext _context;

        public HomeController(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            // Un pedido dividido se cuenta por sus partes, no como un pedido más
            var pedidosDeTrabajo = _context.Pedidos.Where(p => !p.Partes.Any());
            var pendientes = await pedidosDeTrabajo.CountAsync(p => p.Estado == "Pendiente");
            var enProceso = await pedidosDeTrabajo.CountAsync(p => p.Estado == "Asignado" || p.Estado == "EnProceso");
            var completados = await pedidosDeTrabajo.CountAsync(p => p.Estado == "Completado");
            var cancelados = await pedidosDeTrabajo.CountAsync(p => p.Estado == "Cancelado");
            var reasignados = await pedidosDeTrabajo.CountAsync(p => p.Estado == "Reasignado");

            ViewBag.TotalPendientes = pendientes;
            ViewBag.TotalEnProceso = enProceso;
            ViewBag.TotalCompletados = completados;
            ViewBag.TotalCancelados = cancelados;
            ViewBag.TotalReasignados = reasignados;

            // Mismo criterio que los contadores de arriba: el padre dividido no es trabajo por sí mismo, sus partes sí
            var ultimosPedidos = await pedidosDeTrabajo
                .Include(p => p.Bodega)
                .Include(p => p.Empleado)
                .OrderByDescending(p => p.FechaCarga)
                .Take(6)
                .ToListAsync();

            return View(ultimosPedidos);
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
