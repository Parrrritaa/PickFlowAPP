using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickingApp.Data;
using PickingApp.Middleware;
using PickingApp.Models;

namespace PickingApp.Controllers
{
    // [Authorize] sin roles: cualquier usuario con sesión iniciada entra; sin sesión lo manda al login
    [Authorize]
    public class HomeController : Controller
    {
        private readonly ApplicationDbContext _context;

        public HomeController(ApplicationDbContext context)
        {
            _context = context;
        }

        // Dashboard (pantalla de inicio): tarjetas con el total de pedidos por estado y los últimos pedidos
        public async Task<IActionResult> Index()
        {
            // Un pedido dividido se cuenta por sus partes, no como un pedido más
            var pedidosDeTrabajo = _context.Pedidos.Where(p => !p.Partes.Any());
            var pendientes = await pedidosDeTrabajo.CountAsync(p => p.Estado == PedidoEstados.Pendiente);
            var enProceso = await pedidosDeTrabajo.CountAsync(p => p.Estado == PedidoEstados.Asignado);
            var completados = await pedidosDeTrabajo.CountAsync(p => p.Estado == PedidoEstados.Completado);
            var cancelados = await pedidosDeTrabajo.CountAsync(p => p.Estado == PedidoEstados.Cancelado);
            var reasignados = await pedidosDeTrabajo.CountAsync(p => p.Estado == PedidoEstados.Reasignado);

            // ViewBag pasa datos sueltos a la vista (Views/Home/Index.cshtml los muestra en las tarjetas)
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

            // La lista se envía como modelo de la vista (@model en Index.cshtml)
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

        // Destino de ErrorBaseDatosMiddleware. Anónima porque la sesión puede no existir aún (p. ej. en el login)
        // y sin verbo HTTP para atender también los POST que fallaron.
        [AllowAnonymous]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult ErrorBaseDatos()
        {
            var error = HttpContext.Features.Get<IExceptionHandlerPathFeature>();
            if (error == null)
            {
                return RedirectToAction(nameof(Index));
            }

            // Reintentar un POST reenviaría el formulario a medias; en ese caso se vuelve al inicio
            var urlReintento = HttpMethods.IsGet(Request.Method)
                ? error.Path + Request.QueryString
                : Url.Action(nameof(Index))!;

            return View(new ErrorBaseDatosViewModel
            {
                SinConexion = ErroresBaseDatos.EsFallaDeConexion(error.Error),
                UrlReintento = urlReintento
            });
        }
    }
}
