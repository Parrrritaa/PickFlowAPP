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
    public class ReportesController : Controller
    {
        private static readonly string[] EstadosReporte = { PedidoEstados.Completado, PedidoEstados.Cancelado, PedidoEstados.Reasignado };

        private readonly ApplicationDbContext _context;
        private readonly IReportService _reportService;
        private readonly IAuditService _auditService;

        public ReportesController(
            ApplicationDbContext context,
            IReportService reportService,
            IAuditService auditService)
        {
            _context = context;
            _reportService = reportService;
            _auditService = auditService;
        }

        // CU-37: Consultar reportes generados
        public async Task<IActionResult> Index()
        {
            // Listas para los reportes individuales (incluye inactivos para consultar su historial)
            ViewBag.Empleados = await _context.Empleados.OrderBy(e => e.NombreCompleto).ToListAsync();
            ViewBag.Bodegas = await _context.Bodegas.OrderBy(b => b.Codigo).ToListAsync();
            return View();
        }

        // CU-32: Generar reporte de tiempos por empleado (general o de un empleado)
        public async Task<IActionResult> TiemposPorEmpleado(int? empleadoId, DateTime? fechaInicio, DateTime? fechaFin)
        {
            var periodo = new PeriodoReporte(fechaInicio, fechaFin);
            if (!periodo.EsValido) return FiltroInvalido("La fecha inicial no puede ser posterior a la fecha final.");

            var query = PedidosCompletadosEnPeriodo(periodo).Where(p => p.EmpleadoId != null);

            if (empleadoId.HasValue)
            {
                var empleado = await _context.Empleados.FindAsync(empleadoId.Value);
                if (empleado == null) return FiltroInvalido("El empleado seleccionado no existe.");

                var pedidosEmpleado = await query
                    .Where(p => p.EmpleadoId == empleado.Id)
                    .Include(p => p.Bodega)
                    .Include(p => p.Detalles)
                    .OrderByDescending(p => p.FechaFinalizacion)
                    .ToListAsync();

                await RegistrarExportacionAsync("Exportar PDF Reporte Tiempos Empleado",
                    $"Reporte individual de {empleado.NombreCompleto} ({empleado.Identificacion}), periodo: {periodo.Descripcion}. {pedidosEmpleado.Count} pedidos.");

                var pdf = _reportService.GenerarReporteDetalleTiemposPdf(
                    "Reporte Individual de Tiempos de Picking (CU-32)",
                    $"Empleado: {empleado.NombreCompleto} · Doc. {empleado.Identificacion}",
                    pedidosEmpleado, periodo, porEmpleado: true);
                return File(pdf, "application/pdf", $"Reporte_Tiempos_Empleado_{empleado.Identificacion}_{periodo.ParaArchivo}.pdf");
            }

            var pedidos = await query.Include(p => p.Empleado).ToListAsync();
            var datos = pedidos
                .GroupBy(p => p.EmpleadoId)
                .Select(g => new ReporteTiemposEmpleadoItem
                {
                    Identificacion = g.First().Empleado!.Identificacion,
                    Empleado = g.First().Empleado!.NombreCompleto,
                    TotalPedidos = g.Count(),
                    PromedioMinutos = Math.Round(g.Average(p => p.TiempoProcesamientoMinutos!.Value), 1),
                    TiempoMinimo = Math.Round(g.Min(p => p.TiempoProcesamientoMinutos!.Value), 1),
                    TiempoMaximo = Math.Round(g.Max(p => p.TiempoProcesamientoMinutos!.Value), 1)
                })
                .OrderBy(i => i.Empleado)
                .ToList();

            await RegistrarExportacionAsync("Exportar PDF Reporte Tiempos Empleado",
                $"Reporte general de todos los empleados, periodo: {periodo.Descripcion}.");

            var pdfBytes = _reportService.GenerarReporteTiemposPorEmpleadoPdf(datos, periodo);
            return File(pdfBytes, "application/pdf", $"Reporte_Tiempos_Empleados_{periodo.ParaArchivo}.pdf");
        }

        // CU-33: Generar reporte de tiempos de entrega (general o de una bodega)
        public async Task<IActionResult> TiemposGenerales(int? bodegaId, DateTime? fechaInicio, DateTime? fechaFin)
        {
            var periodo = new PeriodoReporte(fechaInicio, fechaFin);
            if (!periodo.EsValido) return FiltroInvalido("La fecha inicial no puede ser posterior a la fecha final.");

            var query = PedidosCompletadosEnPeriodo(periodo);

            if (bodegaId.HasValue)
            {
                var bodega = await _context.Bodegas.FindAsync(bodegaId.Value);
                if (bodega == null) return FiltroInvalido("La bodega seleccionada no existe.");

                var pedidosBodega = await query
                    .Where(p => p.BodegaId == bodega.Id)
                    .Include(p => p.Empleado)
                    .Include(p => p.Detalles)
                    .OrderByDescending(p => p.FechaFinalizacion)
                    .ToListAsync();

                await RegistrarExportacionAsync("Exportar PDF Reporte Tiempos General",
                    $"Reporte individual de la bodega {bodega.Codigo}, periodo: {periodo.Descripcion}. {pedidosBodega.Count} pedidos.");

                var pdf = _reportService.GenerarReporteDetalleTiemposPdf(
                    "Reporte de Tiempos de Entrega por Bodega (CU-33)",
                    $"Bodega: {bodega.Nombre} ({bodega.Codigo})",
                    pedidosBodega, periodo, porEmpleado: false);
                return File(pdf, "application/pdf", $"Reporte_Tiempos_Bodega_{bodega.Codigo}_{periodo.ParaArchivo}.pdf");
            }

            var pedidos = await query.Include(p => p.Bodega).ToListAsync();
            var datos = pedidos
                .GroupBy(p => p.BodegaId)
                .Select(g => new ReporteGeneralTiemposItem
                {
                    Bodega = $"{g.First().Bodega!.Nombre} ({g.First().Bodega!.Codigo})",
                    TotalCompletados = g.Count(),
                    PromedioMinutos = Math.Round(g.Average(p => p.TiempoProcesamientoMinutos!.Value), 1),
                    TiempoTotalMinutos = Math.Round(g.Sum(p => p.TiempoProcesamientoMinutos!.Value), 1)
                })
                .OrderBy(i => i.Bodega)
                .ToList();

            await RegistrarExportacionAsync("Exportar PDF Reporte Tiempos General",
                $"Reporte general de todas las bodegas, periodo: {periodo.Descripcion}.");

            var pdfBytes = _reportService.GenerarReporteGeneralTiemposPdf(datos, periodo);
            return File(pdfBytes, "application/pdf", $"Reporte_Tiempos_Entrega_{periodo.ParaArchivo}.pdf");
        }

        // CU-34: Generar reporte de pedidos por periodo (todas las bodegas o una)
        public async Task<IActionResult> PedidosPorPeriodo(int? bodegaId, DateTime? fechaInicio, DateTime? fechaFin)
        {
            var periodo = new PeriodoReporte(fechaInicio, fechaFin);
            if (!periodo.EsValido) return FiltroInvalido("La fecha inicial no puede ser posterior a la fecha final.");

            // Los pedidos divididos se reportan por sus partes
            var query = _context.Pedidos
                .Include(p => p.Bodega)
                .Include(p => p.Empleado)
                .Where(p => !p.Partes.Any())
                .AsQueryable();

            if (periodo.Desde is DateTime desde) query = query.Where(p => p.FechaCarga >= desde);
            if (periodo.HastaExclusivo is DateTime hasta) query = query.Where(p => p.FechaCarga < hasta);

            string? filtro = null;
            if (bodegaId.HasValue)
            {
                var bodega = await _context.Bodegas.FindAsync(bodegaId.Value);
                if (bodega == null) return FiltroInvalido("La bodega seleccionada no existe.");
                query = query.Where(p => p.BodegaId == bodega.Id);
                filtro = $"Bodega: {bodega.Nombre} ({bodega.Codigo})";
            }

            var pedidos = await query.OrderByDescending(p => p.FechaCarga).ToListAsync();

            await RegistrarExportacionAsync("Exportar PDF Pedidos por Periodo",
                $"{pedidos.Count} pedidos, periodo: {periodo.Descripcion}. {filtro ?? "Todas las bodegas"}.");

            var pdfBytes = _reportService.GenerarReportePedidosPorPeriodoPdf(pedidos, periodo, filtro);
            var sufijo = bodegaId.HasValue ? $"_{pedidos.FirstOrDefault()?.Bodega?.Codigo ?? $"Bodega{bodegaId}"}" : "";
            return File(pdfBytes, "application/pdf", $"Reporte_Pedidos{sufijo}_{periodo.ParaArchivo}.pdf");
        }

        // CU-35: Generar reporte de pedidos completados, cancelados y reasignados (todos o un estado)
        public async Task<IActionResult> CompletadosCanceladosReasignados(string? estado, DateTime? fechaInicio, DateTime? fechaFin)
        {
            var periodo = new PeriodoReporte(fechaInicio, fechaFin);
            if (!periodo.EsValido) return FiltroInvalido("La fecha inicial no puede ser posterior a la fecha final.");

            if (!string.IsNullOrEmpty(estado) && !EstadosReporte.Contains(estado))
            {
                return FiltroInvalido("El estado seleccionado no es válido.");
            }

            // "Reasignados" incluye los que se reasignaron y después se completaron o cancelaron:
            // el estado Reasignado solo dura mientras el pedido sigue en curso.
            var query = _context.Pedidos
                .Include(p => p.Empleado)
                .Where(p => !p.Partes.Any());
            query = estado switch
            {
                PedidoEstados.Reasignado => query.Where(p => p.MotivoReasignacion != null),
                null or "" => query.Where(p => EstadosReporte.Contains(p.Estado) || p.MotivoReasignacion != null),
                _ => query.Where(p => p.Estado == estado)
            };

            if (periodo.Desde is DateTime desde) query = query.Where(p => p.FechaCarga >= desde);
            if (periodo.HastaExclusivo is DateTime hasta) query = query.Where(p => p.FechaCarga < hasta);

            var pedidos = await query.OrderByDescending(p => p.FechaCarga).ToListAsync();
            var filtro = string.IsNullOrEmpty(estado) ? null : $"Solo pedidos {NombreEstadoPlural(estado).ToLowerInvariant()}";

            await RegistrarExportacionAsync("Exportar PDF Completados Cancelados Reasignados",
                $"{pedidos.Count} registros, periodo: {periodo.Descripcion}. {filtro ?? "Todos los estados"}.");

            var pdfBytes = _reportService.GenerarReporteCompletadosCanceladosReasignadosPdf(pedidos, periodo, filtro);
            var nombre = string.IsNullOrEmpty(estado) ? "Completados_Cancelados_Reasignados" : NombreEstadoPlural(estado);
            return File(pdfBytes, "application/pdf", $"Reporte_{nombre}_{periodo.ParaArchivo}.pdf");
        }

        private IQueryable<Pedido> PedidosCompletadosEnPeriodo(PeriodoReporte periodo)
        {
            var query = _context.Pedidos
                .Where(p => p.Estado == PedidoEstados.Completado && p.TiempoProcesamientoMinutos != null);

            if (periodo.Desde is DateTime desde) query = query.Where(p => p.FechaFinalizacion >= desde);
            if (periodo.HastaExclusivo is DateTime hasta) query = query.Where(p => p.FechaFinalizacion < hasta);

            return query;
        }

        private static string NombreEstadoPlural(string estado) => estado switch
        {
            PedidoEstados.Completado => "Completados",
            PedidoEstados.Cancelado => "Cancelados",
            PedidoEstados.Reasignado => "Reasignados",
            _ => estado
        };

        private IActionResult FiltroInvalido(string mensaje)
        {
            TempData["ErrorMessage"] = mensaje;
            return RedirectToAction(nameof(Index));
        }

        private async Task RegistrarExportacionAsync(string accion, string detalle)
        {
            await _auditService.RegistrarAsync(accion, "Reportes", detalle);
        }
    }
}
