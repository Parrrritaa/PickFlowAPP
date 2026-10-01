using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickingApp.Data;
using PickingApp.Middleware;
using PickingApp.Models;
using PickingApp.Services;

namespace PickingApp.Controllers
{
    // [Authorize] sin roles: cualquier usuario con sesión iniciada entra; sin sesión lo manda al login
    [Authorize]
    public class HomeController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IReportService _reportService;
        private readonly IAuditService _auditService;

        public HomeController(ApplicationDbContext context, IReportService reportService, IAuditService auditService)
        {
            _context = context;
            _reportService = reportService;
            _auditService = auditService;
        }

        // Dashboard (pantalla de inicio): tarjetas con el total de pedidos por estado y los últimos pedidos
        public async Task<IActionResult> Index()
        {
            var totales = await ContarPorEstadoAsync();

            // ViewBag pasa datos sueltos a la vista (Views/Home/Index.cshtml los muestra en las tarjetas)
            ViewBag.TotalPendientes = totales.Pendientes;
            ViewBag.TotalEnProceso = totales.EnProceso;
            ViewBag.TotalCompletados = totales.Completados;
            ViewBag.TotalCancelados = totales.Cancelados;
            ViewBag.TotalReasignados = totales.Reasignados;

            // Opciones del filtro de bodega del gráfico
            ViewBag.Bodegas = await _context.Bodegas.OrderBy(b => b.Nombre).ToListAsync();

            // La lista se envía como modelo de la vista (@model en Index.cshtml)
            return View(await UltimosPedidosAsync());
        }

        // Un pedido dividido se cuenta por sus partes, no como un pedido más
        private IQueryable<Pedido> PedidosDeTrabajo() => _context.Pedidos.Where(p => !p.Partes.Any());

        private async Task<(int Pendientes, int EnProceso, int Completados, int Cancelados, int Reasignados)> ContarPorEstadoAsync()
        {
            var pedidosDeTrabajo = PedidosDeTrabajo();
            return (
                await pedidosDeTrabajo.CountAsync(p => p.Estado == PedidoEstados.Pendiente),
                await pedidosDeTrabajo.CountAsync(p => p.Estado == PedidoEstados.Asignado),
                await pedidosDeTrabajo.CountAsync(p => p.Estado == PedidoEstados.Completado),
                await pedidosDeTrabajo.CountAsync(p => p.Estado == PedidoEstados.Cancelado),
                await pedidosDeTrabajo.CountAsync(p => p.Estado == PedidoEstados.Reasignado));
        }

        // Mismo criterio que los contadores: el padre dividido no es trabajo por sí mismo, sus partes sí
        private Task<List<Pedido>> UltimosPedidosAsync() => PedidosDeTrabajo()
            .Include(p => p.Bodega)
            .Include(p => p.Empleado)
            .OrderByDescending(p => p.FechaCarga)
            .Take(5)
            .ToListAsync();

        // Datos del gráfico del dashboard: pedidos de trabajo agrupados por una dimensión y filtrados.
        // Los cuenta igual que las tarjetas (las partes sí, el pedido dividido no). Lo consume wwwroot/js/dashboard-grafica.js.
        [HttpGet]
        public async Task<IActionResult> DatosGrafica(string agrupar = "estado", int dias = 0, int? bodegaId = null, string? prioridad = null, string? estados = null)
        {
            var (total, datos) = await ConsultarGraficaAsync(agrupar, dias, bodegaId, prioridad, estados);
            return Json(new
            {
                total,
                categorias = datos.Select(d => new { clave = d.Clave, etiqueta = d.Etiqueta, valor = d.Valor })
            });
        }

        private static readonly Dictionary<string, string> TitulosGrafica = new()
        {
            ["estado"] = "Pedidos por estado",
            ["prioridad"] = "Pedidos por prioridad",
            ["bodega"] = "Pedidos por bodega",
            ["empleado"] = "Pedidos por empleado (top 8)",
            ["dia"] = "Pedidos cargados por día"
        };

        private static readonly Dictionary<string, string> NombresTipoGrafica = new()
        {
            ["doughnut"] = "dona",
            ["pie"] = "pastel",
            ["bar"] = "barras",
            ["hbar"] = "barras horizontales",
            ["line"] = "líneas",
            ["area"] = "área"
        };

        // PDF con todo lo que muestra el dashboard. La gráfica llega como imagen PNG (la que el usuario está viendo);
        // los números y las tablas se vuelven a calcular aquí con los mismos filtros, así el PDF no depende de lo que mande el navegador.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ExportarDashboardPdf(string agrupar = "estado", int dias = 0, int? bodegaId = null, string? prioridad = null,
            string? estados = null, string tipo = "doughnut", string? imagen = null)
        {
            if (!TitulosGrafica.ContainsKey(agrupar)) agrupar = "estado";
            if (!NombresTipoGrafica.ContainsKey(tipo)) tipo = "doughnut";
            if (dias is < 0 or > 3650) dias = 0;

            var totales = await ContarPorEstadoAsync();
            var (totalGrafica, categorias) = await ConsultarGraficaAsync(agrupar, dias, bodegaId, prioridad, estados);

            // Descripción de los filtros que se ven en el encabezado del PDF
            var filtros = new List<string>();
            if (bodegaId.HasValue)
            {
                var bodega = await _context.Bodegas.FindAsync(bodegaId.Value);
                if (bodega != null) filtros.Add($"Bodega: {bodega.Nombre}");
            }
            if (!string.IsNullOrWhiteSpace(prioridad)) filtros.Add($"Prioridad: {prioridad}");
            var estadosElegidos = (estados ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (estadosElegidos.Length > 0 && estadosElegidos.Length < 5)
                filtros.Add("Estados: " + (estadosElegidos.Contains("-") ? "ninguno" : string.Join(", ", estadosElegidos)));

            var datos = new DashboardPdfDatos
            {
                Pendientes = totales.Pendientes,
                EnProceso = totales.EnProceso,
                Completados = totales.Completados,
                Cancelados = totales.Cancelados,
                Reasignados = totales.Reasignados,
                TituloGrafica = TitulosGrafica[agrupar],
                TipoGrafica = NombresTipoGrafica[tipo],
                Periodo = dias > 0 ? $"Últimos {dias} días" : "Todo el historial",
                Filtros = filtros.Count > 0 ? string.Join(" · ", filtros) : "Sin filtros adicionales",
                TotalGrafica = totalGrafica,
                Categorias = categorias.Select(c => new DashboardCategoriaItem
                {
                    Etiqueta = c.Etiqueta,
                    Valor = c.Valor,
                    Porcentaje = totalGrafica > 0 ? Math.Round(c.Valor * 100.0 / totalGrafica, 1) : 0
                }).ToList(),
                ImagenGrafica = totalGrafica > 0 ? LeerImagenPng(imagen) : null,
                Recientes = await UltimosPedidosAsync()
            };

            await _auditService.RegistrarAsync("Exportar PDF Dashboard", "Dashboard",
                $"{datos.TituloGrafica} ({datos.TipoGrafica}), {datos.Periodo}. {datos.Filtros}.");

            var pdf = _reportService.GenerarReporteDashboardPdf(datos);
            return File(pdf, "application/pdf", $"Dashboard_PickFlow_{DateTime.Now:yyyyMMdd_HHmm}.pdf");
        }

        // La imagen la envía el navegador, así que no se confía en ella: solo se acepta un PNG de tamaño razonable
        private static byte[]? LeerImagenPng(string? dataUrl)
        {
            const string prefijo = "data:image/png;base64,";
            if (string.IsNullOrEmpty(dataUrl) || dataUrl.Length > 4_000_000 || !dataUrl.StartsWith(prefijo, StringComparison.Ordinal))
                return null;

            try
            {
                var bytes = Convert.FromBase64String(dataUrl[prefijo.Length..]);
                byte[] firmaPng = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
                return bytes.Length > firmaPng.Length && bytes.AsSpan(0, firmaPng.Length).SequenceEqual(firmaPng) ? bytes : null;
            }
            catch (FormatException)
            {
                return null;
            }
        }

        // Consulta común del gráfico (pantalla y PDF): total de pedidos filtrados y sus conteos por categoría
        private async Task<(int Total, List<(string Clave, string Etiqueta, int Valor)> Datos)> ConsultarGraficaAsync(
            string agrupar, int dias, int? bodegaId, string? prioridad, string? estados)
        {
            var consulta = PedidosDeTrabajo();

            if (dias > 0)
            {
                var desde = DateTime.Now.Date.AddDays(-(dias - 1));
                consulta = consulta.Where(p => p.FechaCarga >= desde);
            }
            if (bodegaId.HasValue)
                consulta = consulta.Where(p => p.BodegaId == bodegaId.Value);
            if (!string.IsNullOrWhiteSpace(prioridad))
                consulta = consulta.Where(p => p.Prioridad == prioridad);

            var estadosElegidos = (estados ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (estadosElegidos.Length > 0)
                consulta = consulta.Where(p => estadosElegidos.Contains(p.Estado));

            var filas = await consulta
                .Select(p => new
                {
                    p.Estado,
                    p.Prioridad,
                    Bodega = p.Bodega != null ? p.Bodega.Nombre : "Sin bodega",
                    Empleado = p.Empleado != null ? p.Empleado.NombreCompleto : "Sin asignar",
                    p.FechaCarga
                })
                .ToListAsync();

            // Orden fijo en estado y prioridad (con ceros) para que cada color siempre caiga en la misma categoría
            static List<(string Clave, string Etiqueta, int Valor)> Conteo(IEnumerable<string> valores, (string Clave, string Etiqueta)[] orden)
            {
                var cuentas = valores.GroupBy(v => v).ToDictionary(g => g.Key, g => g.Count());
                return orden.Select(o => (o.Clave, o.Etiqueta, cuentas.GetValueOrDefault(o.Clave))).ToList();
            }

            List<(string Clave, string Etiqueta, int Valor)> datos;
            switch (agrupar)
            {
                case "prioridad":
                    datos = Conteo(filas.Select(f => f.Prioridad), new[] { ("Alta", "Alta"), ("Media", "Media"), ("Baja", "Baja") });
                    break;
                case "bodega":
                    datos = filas.GroupBy(f => f.Bodega).OrderByDescending(g => g.Count()).ThenBy(g => g.Key)
                        .Select(g => (g.Key, g.Key, g.Count())).ToList();
                    break;
                case "empleado":
                    datos = filas.GroupBy(f => f.Empleado).OrderByDescending(g => g.Count()).ThenBy(g => g.Key)
                        .Take(8).Select(g => (g.Key, g.Key, g.Count())).ToList();
                    break;
                case "dia":
                    var porDia = filas.GroupBy(f => f.FechaCarga.Date).ToDictionary(g => g.Key, g => g.Count());
                    datos = new();
                    if (porDia.Count > 0)
                    {
                        var inicio = dias > 0 ? DateTime.Now.Date.AddDays(-(dias - 1)) : porDia.Keys.Min();
                        var fin = dias > 0 ? DateTime.Now.Date : porDia.Keys.Max();
                        for (var d = inicio; d <= fin; d = d.AddDays(1))
                            datos.Add((d.ToString("yyyy-MM-dd"), d.ToString("dd/MM"), porDia.GetValueOrDefault(d)));
                    }
                    break;
                default:
                    datos = Conteo(filas.Select(f => f.Estado), new[]
                    {
                        (PedidoEstados.Pendiente, "Pendientes"), (PedidoEstados.Asignado, "En proceso"),
                        (PedidoEstados.Completado, "Completados"), (PedidoEstados.Cancelado, "Cancelados"),
                        (PedidoEstados.Reasignado, "Reasignados")
                    });
                    break;
            }

            return (filas.Count, datos);
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
