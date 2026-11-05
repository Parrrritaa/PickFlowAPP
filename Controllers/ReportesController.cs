using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickingApp.Data;
using PickingApp.Models;
using PickingApp.Models.ViewModels;
using PickingApp.Services;

namespace PickingApp.Controllers
{
    // Reportes en PDF: aquí se consultan y calculan los datos; ReportService solo dibuja el PDF
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
            return View(new ReportesIndexViewModel
            {
                Empleados = await _context.Empleados.ConPasoPorBodega(User).OrderBy(e => e.NombreCompleto).ToListAsync(),
                Bodegas = await _context.Bodegas.DelAlcance(User).OrderBy(b => b.Codigo).ToListAsync()
            });
        }

        // CU-32: Generar reporte de tiempos por empleado (general o de un empleado)
        public async Task<IActionResult> TiemposPorEmpleado(int? empleadoId, DateTime? fechaInicio, DateTime? fechaFin)
        {
            var periodo = new PeriodoReporte(fechaInicio, fechaFin);
            if (!periodo.EsValido) return FiltroInvalido("La fecha inicial no puede ser posterior a la fecha final.");

            var query = PedidosCompletadosEnPeriodo(periodo).Where(p => p.EmpleadoId != null);

            if (empleadoId.HasValue)
            {
                var empleado = await _context.Empleados.ConPasoPorBodega(User).FirstOrDefaultAsync(e => e.Id == empleadoId.Value);
                if (empleado == null) return FiltroInvalido("El empleado seleccionado no existe.");

                var pedidosEmpleado = await query
                    .Where(p => p.EmpleadoId == empleado.Id)
                    .Include(p => p.Bodega)
                    .Include(p => p.Detalles)
                    .OrderByDescending(p => p.FechaFinalizacion)
                    .ToListAsync();

                await _auditService.RegistrarAsync("Exportar PDF Reporte Tiempos Empleado", "Reportes",
                    $"Reporte individual de {empleado.NombreCompleto} ({empleado.Identificacion}), periodo: {periodo.Descripcion}. {pedidosEmpleado.Count} pedidos.");

                var pdf = _reportService.GenerarReporteDetalleTiemposPdf(
                    "Reporte Individual de Tiempos de Picking",
                    $"Empleado: {empleado.NombreCompleto} · Doc. {empleado.Identificacion}",
                    pedidosEmpleado, periodo, porEmpleado: true, await ResumenEstadosAsync(periodo, empleadoId: empleado.Id));
                return File(pdf, "application/pdf", $"Reporte_Tiempos_Empleado_{empleado.Identificacion}_{periodo.ParaArchivo}.pdf");
            }

            // Agrupa los pedidos completados por empleado: cantidad, promedio, mínimo y máximo del tiempo real
            // (TiempoProcesamientoMinutos) y cuántos quedaron dentro de su tiempo estándar
            var datos = ReporteTiemposEmpleadoItem.De(await query.Include(p => p.Empleado).ToListAsync());

            await _auditService.RegistrarAsync("Exportar PDF Reporte Tiempos Empleado", "Reportes",
                $"Reporte general de todos los empleados, periodo: {periodo.Descripcion}.");

            var pdfBytes = _reportService.GenerarReporteTiemposPorEmpleadoPdf(datos, periodo, await ResumenEstadosAsync(periodo));
            return File(pdfBytes, "application/pdf", $"Reporte_Tiempos_Empleados_{periodo.ParaArchivo}.pdf");
        }

        // CU-33: Generar reporte de tiempos de entrega (general o de una bodega)
        public async Task<IActionResult> TiemposGenerales(int? bodegaId, DateTime? fechaInicio, DateTime? fechaFin)
        {
            var periodo = new PeriodoReporte(fechaInicio, fechaFin);
            if (!periodo.EsValido) return FiltroInvalido("La fecha inicial no puede ser posterior a la fecha final.");

            var query = PedidosCompletadosEnPeriodo(periodo);

            var (bodega, error) = await BodegaDelFiltroAsync(bodegaId);
            if (error != null) return error;
            if (bodega != null)
            {
                var pedidosBodega = await query
                    .Where(p => p.BodegaId == bodega.Id)
                    .Include(p => p.Empleado)
                    .Include(p => p.Detalles)
                    .OrderByDescending(p => p.FechaFinalizacion)
                    .ToListAsync();

                await _auditService.RegistrarAsync("Exportar PDF Reporte Tiempos General", "Reportes",
                    $"Reporte individual de la bodega {bodega.Codigo}, periodo: {periodo.Descripcion}. {pedidosBodega.Count} pedidos.");

                var pdf = _reportService.GenerarReporteDetalleTiemposPdf(
                    "Reporte de Tiempos de Entrega por Bodega",
                    $"Bodega: {bodega.Nombre} ({bodega.Codigo})",
                    pedidosBodega, periodo, porEmpleado: false, await ResumenEstadosAsync(periodo, bodegaId: bodega.Id));
                return File(pdf, "application/pdf", $"Reporte_Tiempos_Bodega_{bodega.Codigo}_{periodo.ParaArchivo}.pdf");
            }

            // Misma idea que por empleado, pero agrupando por bodega y sumando el tiempo total
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

            await _auditService.RegistrarAsync("Exportar PDF Reporte Tiempos General", "Reportes",
                $"Reporte general de todas las bodegas, periodo: {periodo.Descripcion}.");

            var pdfBytes = _reportService.GenerarReporteGeneralTiemposPdf(datos, periodo, await ResumenEstadosAsync(periodo));
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

            var (bodega, error) = await BodegaDelFiltroAsync(bodegaId);
            if (error != null) return error;
            string? filtro = null;
            if (bodega != null)
            {
                query = query.Where(p => p.BodegaId == bodega.Id);
                filtro = $"Bodega: {bodega.Nombre} ({bodega.Codigo})";
            }

            var pedidos = await query.OrderByDescending(p => p.FechaCarga).ToListAsync();

            await _auditService.RegistrarAsync("Exportar PDF Pedidos por Periodo", "Reportes",
                $"{pedidos.Count} pedidos, periodo: {periodo.Descripcion}. {filtro ?? "Todas las bodegas"}.");

            var pdfBytes = _reportService.GenerarReportePedidosPorPeriodoPdf(pedidos, periodo, filtro);
            var sufijo = bodega != null ? $"_{bodega.Codigo}" : "";
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
            var query = _context.Pedidos.DelAlcance(User)
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

            await _auditService.RegistrarAsync("Exportar PDF Completados Cancelados Reasignados", "Reportes",
                $"{pedidos.Count} registros, periodo: {periodo.Descripcion}. {filtro ?? "Todos los estados"}.");

            var pdfBytes = _reportService.GenerarReporteCompletadosCanceladosReasignadosPdf(pedidos, periodo, filtro);
            var nombre = string.IsNullOrEmpty(estado) ? "Completados_Cancelados_Reasignados" : NombreEstadoPlural(estado);
            return File(pdfBytes, "application/pdf", $"Reporte_{nombre}_{periodo.ParaArchivo}.pdf");
        }

        // Novedades registradas en el periodo: por tipo, bodega y empleado, y cuáles siguen sin atender
        [Authorize(Roles = Roles.Administrador)]
        public async Task<IActionResult> Novedades(DateTime? fechaInicio, DateTime? fechaFin)
        {
            var periodo = new PeriodoReporte(fechaInicio, fechaFin);
            if (!periodo.EsValido) return FiltroInvalido("La fecha inicial no puede ser posterior a la fecha final.");

            var query = _context.NovedadesPedido
                .Include(n => n.Pedido).ThenInclude(p => p!.Bodega)
                .Include(n => n.Pedido).ThenInclude(p => p!.Empleado)
                .AsQueryable();
            if (periodo.Desde is DateTime desde) query = query.Where(n => n.FechaRegistro >= desde);
            if (periodo.HastaExclusivo is DateTime hasta) query = query.Where(n => n.FechaRegistro < hasta);
            var datos = DatosNovedades.De(await query.ToListAsync());

            await _auditService.RegistrarAsync("Exportar PDF Novedades", "Reportes",
                $"{datos.Total} novedades ({datos.SinAtender} sin atender), periodo: {periodo.Descripcion}.");
            return File(_reportService.GenerarReporteNovedadesPdf(datos, periodo), "application/pdf",
                $"Reporte_Novedades_{periodo.ParaArchivo}.pdf");
        }

        // Solicitudes enviadas en el periodo: cuántas se aprobaron o rechazaron y en cuánto tiempo, por tipo y por revisor
        [Authorize(Roles = Roles.Administrador)]
        public async Task<IActionResult> Solicitudes(DateTime? fechaInicio, DateTime? fechaFin)
        {
            var periodo = new PeriodoReporte(fechaInicio, fechaFin);
            if (!periodo.EsValido) return FiltroInvalido("La fecha inicial no puede ser posterior a la fecha final.");

            var query = _context.Solicitudes
                .Include(s => s.TipoSolicitud)
                .Include(s => s.UsuarioResponde)
                .AsQueryable();
            if (periodo.Desde is DateTime desde) query = query.Where(s => s.FechaSolicitud >= desde);
            if (periodo.HastaExclusivo is DateTime hasta) query = query.Where(s => s.FechaSolicitud < hasta);
            var datos = DatosSolicitudes.De(await query.ToListAsync());

            await _auditService.RegistrarAsync("Exportar PDF Solicitudes", "Reportes",
                $"{datos.Totales.Total} solicitudes, periodo: {periodo.Descripcion}.");
            return File(_reportService.GenerarReporteSolicitudesPdf(datos, periodo), "application/pdf",
                $"Reporte_Solicitudes_{periodo.ParaArchivo}.pdf");
        }

        // Productos y ubicaciones más pedidos (todas las bodegas o una), para acomodar la bodega
        [Authorize(Roles = Roles.AdministradorOSupervisor)]
        public async Task<IActionResult> ProductosUbicaciones(int? bodegaId, DateTime? fechaInicio, DateTime? fechaFin)
        {
            var periodo = new PeriodoReporte(fechaInicio, fechaFin);
            if (!periodo.EsValido) return FiltroInvalido("La fecha inicial no puede ser posterior a la fecha final.");

            // Los divididos cuentan por sus partes (el original repite los mismos productos) y los cancelados no se recogieron
            var query = _context.DetallesPedido
                .Include(d => d.Ubicacion).ThenInclude(u => u!.Bodega)
                .Where(d => !d.Pedido!.Partes.Any() && d.Pedido.Estado != PedidoEstados.Cancelado);
            if (periodo.Desde is DateTime desde) query = query.Where(d => d.Pedido!.FechaCarga >= desde);
            if (periodo.HastaExclusivo is DateTime hasta) query = query.Where(d => d.Pedido!.FechaCarga < hasta);

            var (bodega, error) = await BodegaDelFiltroAsync(bodegaId);
            if (error != null) return error;
            if (bodega != null) query = query.Where(d => d.Pedido!.BodegaId == bodega.Id);

            var datos = DatosProductosUbicaciones.De(await query.ToListAsync());
            var filtro = bodega != null ? $"Bodega: {bodega.Nombre} ({bodega.Codigo})" : null;

            await _auditService.RegistrarAsync("Exportar PDF Productos y Ubicaciones", "Reportes",
                $"{datos.Unidades} unidades, periodo: {periodo.Descripcion}. {filtro ?? "Todas las bodegas"}.");
            return File(_reportService.GenerarReporteProductosUbicacionesPdf(datos, periodo, filtro), "application/pdf",
                $"Reporte_Productos_Ubicaciones{(bodega != null ? "_" + bodega.Codigo : "")}_{periodo.ParaArchivo}.pdf");
        }

        // Carga por hora y día de la semana frente a los empleados en turno, para planear turnos
        [Authorize(Roles = Roles.AdministradorOSupervisor)]
        public async Task<IActionResult> CargaHoraria(int? bodegaId, DateTime? fechaInicio, DateTime? fechaFin)
        {
            var periodo = new PeriodoReporte(fechaInicio, fechaFin);
            if (!periodo.EsValido) return FiltroInvalido("La fecha inicial no puede ser posterior a la fecha final.");

            var query = _context.Pedidos.Where(p => !p.Partes.Any());
            if (periodo.Desde is DateTime desde) query = query.Where(p => p.FechaCarga >= desde);
            if (periodo.HastaExclusivo is DateTime hasta) query = query.Where(p => p.FechaCarga < hasta);

            var (bodega, error) = await BodegaDelFiltroAsync(bodegaId);
            if (error != null) return error;
            if (bodega != null) query = query.Where(p => p.BodegaId == bodega.Id);

            var enTurno = _context.Empleados.Where(e => e.Activo);
            if (bodega != null) enTurno = enTurno.Where(e => e.BodegaId == bodega.Id);
            var datos = DatosCargaHoraria.De(await query.ToListAsync(), await enTurno.ToListAsync());
            var filtro = bodega != null ? $"Bodega: {bodega.Nombre} ({bodega.Codigo})" : null;

            await _auditService.RegistrarAsync("Exportar PDF Carga Horaria", "Reportes",
                $"{datos.Cargados} pedidos, periodo: {periodo.Descripcion}. {filtro ?? "Todas las bodegas"}.");
            return File(_reportService.GenerarReporteCargaHorariaPdf(datos, periodo, filtro), "application/pdf",
                $"Reporte_Carga_Horaria{(bodega != null ? "_" + bodega.Codigo : "")}_{periodo.ParaArchivo}.pdf");
        }

        // Bodega elegida en el filtro (null = todas); si no existe o es de otra, el error para volver a Reportes.
        // Supervisor y Auxiliar no eligen: el reporte siempre es de su bodega, venga lo que venga en la URL.
        private async Task<(Bodega? Bodega, IActionResult? Error)> BodegaDelFiltroAsync(int? bodegaId)
        {
            bodegaId = User.BodegaVisible() ?? bodegaId;
            if (!bodegaId.HasValue) return (null, null);
            var bodega = await _context.Bodegas.DelAlcance(User).FirstOrDefaultAsync(b => b.Id == bodegaId.Value);
            return bodega == null ? (null, FiltroInvalido("La bodega seleccionada no existe.")) : (bodega, null);
        }

        // Pedidos cargados en el periodo, por estado, para el resumen de los reportes de tiempos (que solo listan
        // completados). Los divididos cuentan por sus partes, igual que en el reporte de pedidos por periodo.
        private async Task<ResumenEstadosPedidos> ResumenEstadosAsync(PeriodoReporte periodo, int? empleadoId = null, int? bodegaId = null)
        {
            var query = _context.Pedidos.DelAlcance(User).Where(p => !p.Partes.Any());
            if (periodo.Desde is DateTime desde) query = query.Where(p => p.FechaCarga >= desde);
            if (periodo.HastaExclusivo is DateTime hasta) query = query.Where(p => p.FechaCarga < hasta);
            if (empleadoId.HasValue) query = query.Where(p => p.EmpleadoId == empleadoId.Value);
            if (bodegaId.HasValue) query = query.Where(p => p.BodegaId == bodegaId.Value);

            var porEstado = await query.GroupBy(p => p.Estado).Select(g => new { Estado = g.Key, Cantidad = g.Count() }).ToListAsync();
            var reasignados = await query.CountAsync(p => p.MotivoReasignacion != null);
            return new ResumenEstadosPedidos(porEstado.ToDictionary(e => e.Estado, e => e.Cantidad), reasignados);
        }

        // Base de los reportes de tiempos: solo pedidos completados con tiempo real, filtrados por fecha de finalización
        private IQueryable<Pedido> PedidosCompletadosEnPeriodo(PeriodoReporte periodo)
        {
            var query = _context.Pedidos.DelAlcance(User)
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
    }
}
