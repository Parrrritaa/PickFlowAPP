using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickingApp.Data;
using PickingApp.Models;
using PickingApp.Services;

namespace PickingApp.Controllers
{
    [Authorize]
    // Consulta y ciclo de vida del pedido. El resto de acciones está en los archivos PedidosController.*.cs
    // ("partial" permite repartir una misma clase en varios archivos: Carga, Asignacion, Division, Novedades)
    public partial class PedidosController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IExcelService _excelService;
        private readonly IPickingEngine _pickingEngine;
        private readonly IAuditService _auditService;

        public PedidosController(
            ApplicationDbContext context,
            IExcelService excelService,
            IPickingEngine pickingEngine,
            IAuditService auditService)
        {
            _context = context;
            _excelService = excelService;
            _pickingEngine = pickingEngine;
            _auditService = auditService;
        }

        // CU-24: Consultar pedidos con filtros
        // vista = "divididos" muestra aparte los pedidos divididos con sus partes, para no saturar la lista principal
        public async Task<IActionResult> Index(string? estado, string? prioridad, int? bodegaId, string? busqueda,
            int? empleadoId, DateTime? fechaDesde, DateTime? fechaHasta, string? vista)
        {
            var verDivididos = vista == "divididos";

            // Include trae también los datos relacionados (JOIN), así la vista puede mostrar el nombre de la bodega
            // o del empleado. La consulta aún no se ejecuta: se van sumando filtros y se lanza en ToListAsync
            var query = _context.Pedidos
                .Include(p => p.Bodega)
                .Include(p => p.Empleado)
                .Include(p => p.Partes)
                    .ThenInclude(pa => pa.Empleado)
                .Include(p => p.Partes)
                    .ThenInclude(pa => pa.Detalles)
                .AsSplitQuery()
                .AsQueryable();

            query = verDivididos
                ? query.Where(p => p.Partes.Any())
                : query.Where(p => p.PedidoPadreId == null && !p.Partes.Any());

            // Cada filtro que llega desde el formulario de búsqueda agrega un WHERE a la consulta
            if (!string.IsNullOrEmpty(estado))
            {
                query = query.Where(p => p.Estado == estado);
            }

            if (!string.IsNullOrEmpty(prioridad))
            {
                query = query.Where(p => p.Prioridad == prioridad);
            }

            if (bodegaId.HasValue && bodegaId.Value > 0)
            {
                query = query.Where(p => p.BodegaId == bodegaId.Value);
            }

            if (!string.IsNullOrWhiteSpace(busqueda))
            {
                var b = busqueda.Trim().ToLower();
                query = query.Where(p => p.CodigoPedido.ToLower().Contains(b) ||
                                         p.Detalles.Any(d => d.NombreProducto.ToLower().Contains(b) || d.CodigoProducto.ToLower().Contains(b)));
            }

            // empleadoId = -1 filtra los pedidos sin empleado asignado; en los divididos se mira el empleado de sus partes
            if (empleadoId == -1)
            {
                query = verDivididos
                    ? query.Where(p => p.Partes.Any(pa => pa.EmpleadoId == null && !PedidoEstados.Finalizados.Contains(pa.Estado)))
                    : query.Where(p => p.EmpleadoId == null);
            }
            else if (empleadoId.HasValue && empleadoId.Value > 0)
            {
                query = verDivididos
                    ? query.Where(p => p.Partes.Any(pa => pa.EmpleadoId == empleadoId.Value))
                    : query.Where(p => p.EmpleadoId == empleadoId.Value);
            }

            if (fechaDesde.HasValue)
            {
                var desde = fechaDesde.Value.Date;
                query = query.Where(p => p.FechaCarga >= desde);
            }

            if (fechaHasta.HasValue)
            {
                var hastaExclusivo = fechaHasta.Value.Date.AddDays(1);
                query = query.Where(p => p.FechaCarga < hastaExclusivo);
            }

            // Se listan todas las bodegas (no solo las activas) para poder consultar pedidos históricos de bodegas desactivadas
            ViewBag.Bodegas = await _context.Bodegas.OrderBy(b => b.Codigo).ToListAsync();
            ViewBag.Empleados = await _context.Empleados.OrderBy(e => e.NombreCompleto).ToListAsync();
            ViewBag.FiltroEstado = estado;
            ViewBag.FiltroPrioridad = prioridad;
            ViewBag.FiltroBodegaId = bodegaId;
            ViewBag.FiltroEmpleadoId = empleadoId;
            ViewBag.FiltroFechaDesde = fechaDesde?.ToString("yyyy-MM-dd");
            ViewBag.FiltroFechaHasta = fechaHasta?.ToString("yyyy-MM-dd");
            ViewBag.Busqueda = busqueda;
            ViewBag.Vista = verDivididos ? "divididos" : null;
            ViewBag.DivididosEnCurso = await _context.Pedidos.CountAsync(p => p.Estado == PedidoEstados.Dividido);

            // Aquí se ejecuta el SELECT en MySQL. Orden: primero prioridad Alta, luego los más recientes
            var pedidos = await query
                .OrderByDescending(p => p.Prioridad == "Alta")
                .ThenByDescending(p => p.FechaCarga)
                .ThenBy(p => p.CodigoPedido)
                .ToListAsync();

            return View(pedidos);
        }

        // Detalle de Pedido (con ruta de picking de ubicaciones, productos, novedades)
        public async Task<IActionResult> Detalle(int id)
        {
            var pedido = await _context.Pedidos
                .Include(p => p.Bodega)
                .Include(p => p.Empleado)
                .Include(p => p.Detalles)
                    .ThenInclude(d => d.Ubicacion)
                        .ThenInclude(u => u!.Producto)
                            .ThenInclude(pr => pr!.Categoria)
                .Include(p => p.Novedades)
                .Include(p => p.PedidoPadre)
                    .ThenInclude(pp => pp!.Partes)
                .Include(p => p.Partes)
                    .ThenInclude(pa => pa.Empleado)
                .Include(p => p.Partes)
                    .ThenInclude(pa => pa.Detalles)
                .Include(p => p.Partes)
                    .ThenInclude(pa => pa.Novedades)
                .AsSplitQuery()
                .FirstOrDefaultAsync(p => p.Id == id);

            if (pedido == null) return NotFound();

            // Tiempo estándar según las categorías de los productos, comparado con una jornada completa
            ViewBag.TiempoEstandar = pedido.TiempoEstandarMinutos ?? await _pickingEngine.CalcularTiempoEstandarAsync(pedido.Id);
            ViewBag.CategoriaPredeterminada = await _context.CategoriasProducto
                .Where(c => c.EsPredeterminada).Select(c => c.Nombre).FirstOrDefaultAsync();
            ViewBag.MaxHorasJornada = await ObtenerMaxHorasJornadaAsync();
            ViewBag.TiemposPartes = await _pickingEngine.CalcularTiemposEstandarAsync(pedido.Partes.Select(pa => pa.Id).ToList());

            return View(pedido);
        }

        // CU-28: Marcar pedido como completado
        [Authorize(Roles = Roles.AdministradorOSupervisor)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarcarCompletado(int pedidoId, bool confirmacionFisica)
        {
            if (!confirmacionFisica)
            {
                TempData["ErrorMessage"] = "El pedido no puede marcarse como completado porque no existe confirmación física de la entrega.";
                return RedirectToAction(nameof(Detalle), new { id = pedidoId });
            }

            var pedido = await _context.Pedidos.FindAsync(pedidoId);
            if (pedido == null) return NotFound();

            if (!pedido.EstaEnCurso)
            {
                TempData["ErrorMessage"] = "Solo se puede completar un pedido que esté asignado a un empleado.";
                return RedirectToAction(nameof(Detalle), new { id = pedidoId });
            }

            // Cálculo del tiempo real de picking (PickingEngine.CalcularTiempoProcesamiento).
            // El tiempo real cuenta desde el inicio fijado al asignar (asignación + margen de ese momento)
            var fechaFin = DateTime.Now;
            var inicioProceso = pedido.FechaInicioProceso
                ?? (pedido.FechaAsignacion ?? fechaFin.AddMinutes(-10)).AddMinutes(await ObtenerMargenInicioAsync());
            var tiempoReal = _pickingEngine.CalcularTiempoProcesamiento(inicioProceso, fechaFin);

            pedido.Estado = PedidoEstados.Completado;
            pedido.ConfirmacionFisica = true;
            pedido.FechaFinalizacion = fechaFin;
            pedido.TiempoProcesamientoMinutos = tiempoReal;

            var padreCerrado = await CerrarPedidoDivididoSiTerminoAsync(pedido.PedidoPadreId);

            // Guarda en una sola transacción el pedido completado (y el pedido original si se cerró)
            await _context.SaveChangesAsync();

            await _auditService.RegistrarAsync("Completar Pedido", "Pedidos",
                $"Pedido {pedido.CodigoPedido} marcado como completado con entrega física. Tiempo real calculado: {tiempoReal} min");
            await RegistrarCierrePedidoDivididoAsync(padreCerrado);

            TempData["SuccessMessage"] = $"Pedido '{pedido.CodigoPedido}' completado exitosamente. Tiempo de procesamiento: {tiempoReal} minutos.";
            return RedirectToAction(nameof(Detalle), new { id = pedidoId });
        }

        // CU-31: Cancelar pedido
        [Authorize(Roles = Roles.AdministradorOSupervisor)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Cancelar(int pedidoId, string motivoCancelacion)
        {
            if (string.IsNullOrWhiteSpace(motivoCancelacion))
            {
                TempData["ErrorMessage"] = "El motivo de cancelación es obligatorio.";
                return RedirectToAction(nameof(Detalle), new { id = pedidoId });
            }

            var pedido = await _context.Pedidos.FindAsync(pedidoId);
            if (pedido == null) return NotFound();

            if (pedido.Estado == PedidoEstados.Cancelado)
            {
                TempData["ErrorMessage"] = "El pedido ya se encuentra cancelado y no puede reactivarse.";
                return RedirectToAction(nameof(Detalle), new { id = pedidoId });
            }

            if (pedido.Estado == PedidoEstados.Completado)
            {
                TempData["ErrorMessage"] = "El pedido ya se encuentra completado y no puede cancelarse.";
                return RedirectToAction(nameof(Detalle), new { id = pedidoId });
            }

            // Pedido dividido: se cancelan también las partes que no hayan terminado; las completadas se conservan
            var partesCanceladas = 0;
            if (pedido.EstaDividido)
            {
                var partesActivas = await _context.Pedidos
                    .Where(pa => pa.PedidoPadreId == pedido.Id && !PedidoEstados.Finalizados.Contains(pa.Estado))
                    .ToListAsync();
                foreach (var parte in partesActivas)
                {
                    parte.Estado = PedidoEstados.Cancelado;
                    parte.MotivoCancelacion = $"Cancelado junto con el pedido {pedido.CodigoPedido}: {motivoCancelacion.Trim()}";
                    if (parte.MotivoCancelacion.Length > 250) parte.MotivoCancelacion = parte.MotivoCancelacion[..250];
                    parte.FechaFinalizacion = DateTime.Now;
                }
                partesCanceladas = partesActivas.Count;
            }

            pedido.Estado = PedidoEstados.Cancelado;
            pedido.MotivoCancelacion = motivoCancelacion.Trim();
            pedido.FechaFinalizacion = DateTime.Now;

            var padreCerrado = await CerrarPedidoDivididoSiTerminoAsync(pedido.PedidoPadreId);

            await _context.SaveChangesAsync();

            await _auditService.RegistrarAsync("Cancelar Pedido", "Pedidos",
                $"Pedido {pedido.CodigoPedido} cancelado{(partesCanceladas > 0 ? $" junto con {partesCanceladas} parte(s) sin terminar" : "")}. Motivo: {motivoCancelacion}");
            await RegistrarCierrePedidoDivididoAsync(padreCerrado);

            TempData["SuccessMessage"] = partesCanceladas > 0
                ? $"Pedido '{pedido.CodigoPedido}' y {partesCanceladas} parte(s) sin terminar cancelados correctamente."
                : $"Pedido '{pedido.CodigoPedido}' cancelado correctamente.";
            return RedirectToAction(nameof(Detalle), new { id = pedidoId });
        }

        // CU-25: Cambiar prioridad del pedido (Solo Administrador)
        [Authorize(Roles = Roles.Administrador)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CambiarPrioridad(int pedidoId, string nuevaPrioridad)
        {
            var pedido = await _context.Pedidos.FindAsync(pedidoId);
            if (pedido == null) return NotFound();

            var prioridadAnterior = pedido.Prioridad;
            pedido.Prioridad = nuevaPrioridad;

            if (pedido.EstaDividido)
            {
                await _context.Pedidos
                    .Where(pa => pa.PedidoPadreId == pedido.Id && !PedidoEstados.Finalizados.Contains(pa.Estado))
                    .ForEachAsync(pa => pa.Prioridad = nuevaPrioridad);
            }

            await _context.SaveChangesAsync();

            await _auditService.RegistrarAsync("Cambiar Prioridad Pedido", "Pedidos", 
                $"Prioridad del pedido {pedido.CodigoPedido} modificada de {prioridadAnterior} a {nuevaPrioridad}");

            TempData["SuccessMessage"] = $"Prioridad cambiada a '{nuevaPrioridad}' para el pedido {pedido.CodigoPedido}.";
            return RedirectToAction(nameof(Detalle), new { id = pedidoId });
        }

        // Solo modifica el contexto (la parte recién finalizada ya está rastreada con su nuevo estado), así el
        // cierre se guarda en el mismo SaveChanges que la parte. Devuelve el pedido original si se cerró.
        private async Task<Pedido?> CerrarPedidoDivididoSiTerminoAsync(int? pedidoPadreId)
        {
            if (pedidoPadreId == null) return null;

            var padre = await _context.Pedidos
                .Include(p => p.Partes)
                .FirstOrDefaultAsync(p => p.Id == pedidoPadreId);

            return padre != null && padre.CerrarSiTerminaronLasPartes() ? padre : null;
        }

        private async Task RegistrarCierrePedidoDivididoAsync(Pedido? padre)
        {
            if (padre == null) return;

            var completado = padre.Estado == PedidoEstados.Completado;
            await _auditService.RegistrarAsync(completado ? "Completar Pedido" : "Cancelar Pedido", "Pedidos",
                $"Pedido {padre.CodigoPedido} {(completado ? "completado" : "cancelado")} automáticamente: finalizaron sus {padre.Partes.Count} partes.");
        }

        private Task<double> ObtenerMaxHorasJornadaAsync() =>
            ParametrosSistema.ObtenerDecimalAsync(_context, ParametrosSistema.MaxHorasJornada);
    }
}
