using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using PickingApp.Models;
using System.IO;

namespace PickingApp.Services
{
    // Arma los reportes en PDF con la librería QuestPDF. Solo dibuja: los datos ya llegan calculados
    // desde ReportesController, que devuelve los bytes al navegador como archivo descargable.
    // Se registra como Singleton (Program.cs): la licencia y el logo se cargan una sola vez.
    public class ReportService : IReportService
    {
        // Logo del encabezado, o null si el archivo no está (el PDF sale igual, solo con el nombre)
        private readonly byte[]? _logo;

        public ReportService(IWebHostEnvironment env, ILogger<ReportService> logger)
        {
            // Licencia gratuita de QuestPDF para proyectos pequeños y académicos
            QuestPDF.Settings.License = LicenseType.Community;

            // WebRootPath apunta a wwwroot tanto con "dotnet run" (carpeta del proyecto) como publicado
            // (carpeta de publicación); la carpeta bin no tiene copia de wwwroot
            var logoPath = Path.Combine(env.WebRootPath, "images", "pickflow-logo.jpg");
            if (File.Exists(logoPath))
            {
                _logo = File.ReadAllBytes(logoPath);
            }
            else
            {
                logger.LogWarning("No se encontró el logo de los reportes en {Ruta}; los PDF saldrán sin logo.", logoPath);
            }
        }

        public byte[] GenerarReporteTiemposPorEmpleadoPdf(List<ReporteTiemposEmpleadoItem> datos, PeriodoReporte periodo, ResumenEstadosPedidos estados)
        {
            var completados = datos.Sum(i => i.TotalPedidos);
            // Totales del estándar sumando los de cada empleado: cada uno pesa según sus pedidos
            var total = new ReporteTiemposEmpleadoItem
            {
                ConEstandar = datos.Sum(i => i.ConEstandar),
                DentroDelEstandar = datos.Sum(i => i.DentroDelEstandar),
                MinutosRealesConEstandar = datos.Sum(i => i.MinutosRealesConEstandar),
                MinutosEstandar = datos.Sum(i => i.MinutosEstandar)
            };

            return Pdf("Reporte de Tiempos de Picking por Empleado", periodo.Descripcion, "Todos los empleados", c => c.Column(col =>
            {
                col.Spacing(14);
                col.Item().Element(t => FilaMetricas(t, "Tiempos del periodo",
                    "Pedidos completados por un empleado, según su fecha de finalización. Cumplimiento: los que tardaron lo mismo o menos que su tiempo estándar.",
                    new("Completados", completados.ToString(), ColorCompletado),
                    // Promedio ponderado: cada empleado pesa según cuántos pedidos completó
                    new("Promedio", completados > 0 ? $"{datos.Sum(i => i.PromedioMinutos * i.TotalPedidos) / completados:0.0} min" : "-", AzulMarca),
                    new("Más rápido", datos.Any() ? $"{datos.Min(i => i.TiempoMinimo):0.0} min" : "-", AzulMarca),
                    new("Más lento", datos.Any() ? $"{datos.Max(i => i.TiempoMaximo):0.0} min" : "-", AzulMarca),
                    new("Cumplimiento", Porcentaje(total.PorcentajeCumplimiento), ColorCompletado),
                    new("Desviación", Desviacion(total.DesviacionPorcentaje), ColorEnProceso)));
                col.Item().Element(t => ResumenEstados(t, estados));
                col.Item().Element(t => Tabla(t, datos, "No hay pedidos completados por empleados en este periodo.",
                    new Columna<ReporteTiemposEmpleadoItem>("Doc ID", i => i.Identificacion, 80, Fija: true),
                    new("Empleado", i => i.Empleado, 2.6f),
                    new("Pedidos", i => i.TotalPedidos.ToString(), 1.2f, Derecha: true),
                    new("Promedio (m)", i => i.PromedioMinutos.ToString("0.0"), 1.6f, Derecha: true),
                    new("Mínimo (m)", i => i.TiempoMinimo.ToString("0.0"), 1.5f, Derecha: true),
                    new("Máximo (m)", i => i.TiempoMaximo.ToString("0.0"), 1.5f, Derecha: true),
                    new("Cumplimiento", i => Porcentaje(i.PorcentajeCumplimiento), 1.8f, Derecha: true),
                    new("Desviación", i => Desviacion(i.DesviacionPorcentaje), 1.6f, Derecha: true)));
                col.Item().Text("Desviación: cuánto más (+) o menos (−) tarda el empleado frente al tiempo estándar de sus pedidos.")
                   .FontSize(8).FontColor(Colors.Grey.Darken1);
            }), horizontal: true);
        }

        public byte[] GenerarReporteGeneralTiemposPdf(List<ReporteGeneralTiemposItem> datos, PeriodoReporte periodo, ResumenEstadosPedidos estados)
        {
            var completados = datos.Sum(i => i.TotalCompletados);
            var minutos = datos.Sum(i => i.TiempoTotalMinutos);
            return Pdf("Reporte General de Tiempos de Entrega", periodo.Descripcion, "Todas las bodegas", c => c.Column(col =>
            {
                col.Spacing(14);
                col.Item().Element(t => FilaMetricas(t, "Tiempos del periodo", "Pedidos completados, según su fecha de finalización.",
                    new("Completados", completados.ToString(), ColorCompletado),
                    new("Bodegas", datos.Count.ToString(), ColorTotal),
                    new("Promedio", completados > 0 ? $"{minutos / completados:0.0} min" : "-", AzulMarca),
                    new("Tiempo total", $"{minutos / 60:0.0} h", AzulMarca)));
                col.Item().Element(t => ResumenEstados(t, estados));
                col.Item().Element(t => Tabla(t, datos, "No hay pedidos completados en este periodo.",
                    new Columna<ReporteGeneralTiemposItem>("Bodega", i => i.Bodega, 3),
                    new("Pedidos Completados", i => i.TotalCompletados.ToString(), 2, Derecha: true),
                    new("Promedio (min)", i => i.PromedioMinutos.ToString("0.0"), 2, Derecha: true),
                    new("Total Minutos", i => i.TiempoTotalMinutos.ToString("0.0"), 2, Derecha: true)));
            }));
        }

        public byte[] GenerarReportePedidosPorPeriodoPdf(List<Pedido> pedidos, PeriodoReporte periodo, string? filtro) =>
            Pdf("Reporte Consolidado de Pedidos por Periodo", periodo.Descripcion, filtro ?? "Todas las bodegas", c => c.Column(col =>
            {
                col.Spacing(14);
                col.Item().Element(t => ResumenEstados(t, ResumenEstadosPedidos.De(pedidos)));
                col.Item().Element(t => Tabla(t, pedidos, "No hay pedidos en este periodo.",
                    new Columna<Pedido>("Código", p => p.CodigoPedido, 90, Fija: true),
                    new("Bodega", p => p.Bodega?.Nombre ?? "N/A", 2),
                    new("Prioridad", p => p.Prioridad, 60, Fija: true),
                    new("Estado", p => p.Estado, 80, Fija: true),
                    new("Empleado", p => p.Empleado?.NombreCompleto ?? "Sin asignar", 2),
                    new("Fecha Carga", p => p.FechaCarga.ToString("dd/MM/yyyy HH:mm"), 100, Fija: true),
                    new("Tiempo (m)", p => p.TiempoProcesamientoMinutos?.ToString("0.0") ?? "-", 70, Fija: true, Derecha: true)));
            }), horizontal: true);

        public byte[] GenerarReporteCompletadosCanceladosReasignadosPdf(List<Pedido> pedidos, PeriodoReporte periodo, string? filtro)
        {
            var completados = pedidos.Where(p => p.Estado == PedidoEstados.Completado).ToList();
            var cancelados = pedidos.Count(p => p.Estado == PedidoEstados.Cancelado);
            var tiempos = completados.Where(p => p.TiempoProcesamientoMinutos.HasValue).Select(p => p.TiempoProcesamientoMinutos!.Value).ToList();
            var cerrados = completados.Count + cancelados;

            return Pdf("Reporte de Pedidos Completados, Cancelados y Reasignados", periodo.Descripcion, filtro ?? "Completados, cancelados y reasignados",
                c => c.Column(col =>
                {
                    col.Spacing(14);
                    col.Item().Element(t => FilaMetricas(t, "Resumen del periodo",
                        "Pedidos cargados en el periodo. Un pedido reasignado también cuenta en su estado final.",
                        new("Total", pedidos.Count.ToString(), ColorTotal),
                        new("Completados", completados.Count.ToString(), ColorCompletado),
                        new("Cancelados", cancelados.ToString(), ColorCancelado),
                        new("Reasignados", pedidos.Count(p => p.MotivoReasignacion != null).ToString(), ColorReasignado),
                        // De los que ya se cerraron, cuántos se entregaron (no se cancelaron)
                        new("Cumplimiento", cerrados > 0 ? $"{100.0 * completados.Count / cerrados:0}%" : "-", ColorCompletado),
                        new("Tiempo promedio", tiempos.Any() ? $"{tiempos.Average():0.0} min" : "-", AzulMarca)));
                    col.Item().Element(t => Tabla(t, pedidos, "No hay pedidos con estos estados en este periodo.",
                        new Columna<Pedido>("Código", p => p.CodigoPedido, 90, Fija: true),
                        new("Estado", p => p.Estado, 80, Fija: true),
                        new("Empleado", p => p.Empleado?.NombreCompleto ?? "-", 2),
                        new("Fecha Fin", p => p.FechaFinalizacion?.ToString("dd/MM/yyyy HH:mm") ?? "-", 100, Fija: true),
                        new("Tiempo", p => Minutos(p.TiempoProcesamientoMinutos), 70, Fija: true, Derecha: true),
                        new("Motivo / Observación", p =>
                            !string.IsNullOrEmpty(p.MotivoCancelacion) ? $"Cancelación: {p.MotivoCancelacion}"
                            : !string.IsNullOrEmpty(p.MotivoReasignacion) ? $"Reasignación: {p.MotivoReasignacion}"
                            : p.ConfirmacionFisica ? "Entrega física confirmada" : "-", 3)));
                }), horizontal: true);
        }

        public byte[] GenerarReporteDetalleTiemposPdf(string titulo, string sujeto, List<Pedido> pedidos, PeriodoReporte periodo, bool porEmpleado,
            ResumenEstadosPedidos estados)
        {
            var tiempos = pedidos.Where(p => p.TiempoProcesamientoMinutos.HasValue)
                                 .Select(p => p.TiempoProcesamientoMinutos!.Value)
                                 .ToList();
            var unidades = pedidos.Sum(p => p.Detalles.Sum(d => d.Cantidad));
            var conEstandar = pedidos.Where(p => p.TiempoEstandarMinutos > 0 && p.TiempoProcesamientoMinutos.HasValue).ToList();
            var cumplimiento = conEstandar.Any()
                ? 100.0 * conEstandar.Count(p => p.TiempoProcesamientoMinutos <= p.TiempoEstandarMinutos) / conEstandar.Count
                : (double?)null;

            return Pdf(titulo, periodo.Descripcion, sujeto, c => c.Column(col =>
            {
                col.Spacing(14);
                col.Item().Element(t => FilaMetricas(t, "Tiempos del periodo",
                    "Pedidos completados, según su fecha de finalización. Cumplimiento: los que tardaron lo mismo o menos que su tiempo estándar.",
                    new("Completados", pedidos.Count.ToString(), ColorCompletado),
                    new("Unidades", unidades.ToString(), ColorTotal),
                    new("Promedio", tiempos.Any() ? $"{tiempos.Average():0.0} min" : "-", AzulMarca),
                    new("Mínimo", tiempos.Any() ? $"{tiempos.Min():0.0} min" : "-", AzulMarca),
                    new("Máximo", tiempos.Any() ? $"{tiempos.Max():0.0} min" : "-", AzulMarca),
                    new("Cumplimiento", Porcentaje(cumplimiento), ColorCompletado)));
                col.Item().Element(t => ResumenEstados(t, estados));

                col.Item().Element(t => Tabla(t, pedidos, "No hay pedidos completados en este periodo.",
                    new Columna<Pedido>("Pedido", p => p.CodigoPedido, 90, Fija: true),
                    new(porEmpleado ? "Bodega" : "Empleado", p => porEmpleado
                        ? (p.Bodega != null ? $"{p.Bodega.Nombre} ({p.Bodega.Codigo})" : "Sin bodega")
                        : (p.Empleado?.NombreCompleto ?? "Sin asignar"), 3),
                    new("Finalizado", p => p.FechaFinalizacion?.ToString("dd/MM/yyyy HH:mm") ?? "-", 95, Fija: true),
                    new("Unidades", p => p.Detalles.Sum(d => d.Cantidad).ToString(), 55, Fija: true, Derecha: true),
                    new("Ubicaciones", p => p.Detalles.Select(d => d.UbicacionId).Distinct().Count().ToString(), 65, Fija: true, Derecha: true),
                    new("Tiempo", p => Minutos(p.TiempoProcesamientoMinutos), 60, Fija: true, Derecha: true),
                    new("Estándar", p => Minutos(p.TiempoEstandarMinutos), 65, Fija: true, Derecha: true)));
            }), horizontal: true);
        }

        public byte[] GenerarReporteLogSistemaPdf(List<LogSistema> logs, string? modulo, string? usuario, DateTime fechaDesde, DateTime fechaHasta)
        {
            var filtros = new List<string>();
            if (!string.IsNullOrWhiteSpace(modulo)) filtros.Add($"Módulo: {modulo}");
            if (!string.IsNullOrWhiteSpace(usuario)) filtros.Add($"Usuario: {usuario}");
            filtros.Add($"{logs.Count} registro(s)");
            var masFrecuente = logs.GroupBy(l => l.Accion).OrderByDescending(g => g.Count()).FirstOrDefault();

            return Pdf("LOG del Sistema", new PeriodoReporte(fechaDesde, fechaHasta).Descripcion, null, c => c.Column(col =>
            {
                col.Item().PaddingBottom(14).Element(t => FilaMetricas(t, "Resumen del periodo", "Actividad registrada con los filtros aplicados.",
                    new("Registros", logs.Count.ToString(), ColorTotal),
                    new("Usuarios", logs.Select(l => l.UsuarioParaMostrar).Distinct().Count().ToString(), AzulMarca),
                    new("Módulos", logs.Select(l => l.Modulo).Distinct().Count().ToString(), AzulMarca),
                    new("Acción más frecuente", masFrecuente != null ? $"{masFrecuente.Key} ({masFrecuente.Count()})" : "-", ColorEnProceso)));
                col.Item().PaddingBottom(6).Text(string.Join(" • ", filtros)).FontSize(8).FontColor(Colors.Grey.Medium);
                col.Item().Element(t => Tabla(t, logs, "No hay registros en el LOG con estos filtros.",
                    new Columna<LogSistema>("Fecha", l => l.FechaHora.ToString("dd/MM/yyyy HH:mm"), 90, Fija: true),
                    new("Usuario", l => l.UsuarioParaMostrar, 2),
                    new("Acción", l => l.Accion, 2),
                    new("Descripción", l => l.Detalle, 4)));
            }), horizontal: true);
        }

        public byte[] GenerarReporteDashboardPdf(DashboardPdfDatos datos) =>
            Pdf("Reporte general del dashboard", datos.Periodo, datos.Filtros, c => c.Column(col =>
            {
                col.Spacing(14);

                // 1) Tarjetas: totales generales, no dependen de los filtros de la gráfica
                col.Item().ShowEntire().Column(sec =>
                {
                    sec.Item().Element(t => TituloSeccion(t, "Resumen general", "Totales actuales de los pedidos de trabajo (un pedido dividido cuenta por sus partes)."));
                    sec.Item().PaddingTop(6).Row(row =>
                    {
                        row.Spacing(6);
                        TarjetaMetrica(row.RelativeItem(), "Total", datos.Total.ToString(), ColorTotal);
                        TarjetaMetrica(row.RelativeItem(), "Pendientes", datos.Pendientes.ToString(), ColorPendiente);
                        TarjetaMetrica(row.RelativeItem(), "En proceso", datos.EnProceso.ToString(), ColorEnProceso);
                        TarjetaMetrica(row.RelativeItem(), "Completados", datos.Completados.ToString(), ColorCompletado);
                        TarjetaMetrica(row.RelativeItem(), "Cancelados", datos.Cancelados.ToString(), ColorCancelado);
                        TarjetaMetrica(row.RelativeItem(), "Reasignados", datos.Reasignados.ToString(), ColorReasignado);
                    });
                });

                // 2) Gráfica con los filtros que tenía el usuario en pantalla
                col.Item().Element(t => TituloSeccion(t, datos.TituloGrafica,
                    $"Gráfica: {datos.TipoGrafica} · {datos.TotalGrafica} {(datos.TotalGrafica == 1 ? "pedido" : "pedidos")} con los filtros aplicados."));

                if (datos.TotalGrafica == 0)
                {
                    col.Item().Element(t => FilaSinDatos(t, "No hay pedidos con los filtros seleccionados."));
                }
                else
                {
                    if (datos.ImagenGrafica != null)
                    {
                        col.Item().ShowEntire().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(6)
                           .Height(235).AlignCenter().AlignMiddle().Image(datos.ImagenGrafica).FitArea();
                    }

                    // Tabla a mano: lleva una fila final de total
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(4);
                            columns.RelativeColumn(1.5f);
                            columns.RelativeColumn(1.5f);
                        });

                        table.Header(header =>
                        {
                            header.Cell().Element(CellStyleHeader).Text("Categoría");
                            header.Cell().Element(CellStyleHeader).AlignRight().Text("Pedidos");
                            header.Cell().Element(CellStyleHeader).AlignRight().Text("%");
                        });

                        foreach (var item in datos.Categorias)
                        {
                            table.Cell().Element(CellStyleRow).Text(item.Etiqueta);
                            table.Cell().Element(CellStyleRow).AlignRight().Text(item.Valor.ToString());
                            table.Cell().Element(CellStyleRow).AlignRight().Text($"{item.Porcentaje:0.#}%");
                        }

                        table.Cell().Element(CellStyleRow).Text("Total").SemiBold();
                        table.Cell().Element(CellStyleRow).AlignRight().Text(datos.TotalGrafica.ToString()).SemiBold();
                        table.Cell().Element(CellStyleRow).AlignRight().Text("");
                    });
                }

                // 3) Pedidos recientes (la misma tabla del dashboard, con bodega y fecha de carga)
                col.Item().EnsureSpace(120).Element(t => TituloSeccion(t, "Pedidos recientes", "Los últimos pedidos cargados."));
                col.Item().Element(t => Tabla(t, datos.Recientes, "No hay pedidos registrados.",
                    new Columna<Pedido>("Pedido", p => p.CodigoPedido, 2),
                    new("Prioridad", p => p.Prioridad, 1.2f),
                    new("Estado", p => p.Estado, 1.5f),
                    new("Empleado", p => p.Empleado?.NombreCompleto ?? "Sin asignar", 2.4f),
                    new("Bodega", p => p.Bodega?.Nombre ?? "-", 2),
                    new("Cargado", p => p.FechaCarga.ToString("dd/MM/yyyy HH:mm"), 1.7f)));
            }));

        public byte[] GenerarReporteNovedadesPdf(DatosNovedades datos, PeriodoReporte periodo) =>
            Pdf("Reporte de Novedades", periodo.Descripcion, "Todas las bodegas", c => c.Column(col =>
            {
                col.Spacing(14);
                col.Item().Element(t => FilaMetricas(t, "Resumen del periodo", "Novedades registradas en el periodo.",
                    new("Novedades", datos.Total.ToString(), ColorTotal),
                    new("Sin atender", datos.SinAtender.ToString(), ColorCancelado),
                    new("Atendidas", datos.Atendidas.ToString(), ColorCompletado),
                    new("Tiempo de atención", Horas(datos.HorasPromedioAtencion), AzulMarca),
                    new("Tipo más común", datos.PorTipo.FirstOrDefault()?.Grupo ?? "-", ColorEnProceso)));

                void Grupo(string titulo, string columna, List<FilaNovedades> filas) =>
                    col.Item().Column(sec =>
                    {
                        sec.Item().EnsureSpace(90).Element(t => TituloSeccion(t, titulo, "Tiempo de atención: desde que se registró hasta que se atendió."));
                        sec.Item().PaddingTop(6).Element(t => Tabla(t, filas, "No hay novedades en este periodo.",
                            new Columna<FilaNovedades>(columna, f => f.Grupo, 3),
                            new("Total", f => f.Total.ToString(), 1, Derecha: true),
                            new("Sin atender", f => f.SinAtender.ToString(), 1.2f, Derecha: true),
                            new("Atendidas", f => f.Atendidas.ToString(), 1.2f, Derecha: true),
                            new("Tiempo de atención", f => Horas(f.HorasPromedioAtencion), 1.6f, Derecha: true)));
                    });

                Grupo("Por tipo", "Tipo", datos.PorTipo);
                Grupo("Por bodega", "Bodega", datos.PorBodega);
                Grupo("Por empleado", "Empleado del pedido", datos.PorEmpleado);

                col.Item().Column(sec =>
                {
                    sec.Item().EnsureSpace(90).Element(t => TituloSeccion(t, "Sin atender", "Las más antiguas primero."));
                    sec.Item().PaddingTop(6).Element(t => Tabla(t, datos.SinAtenderDetalle, "Todas las novedades del periodo están atendidas.",
                        new Columna<NovedadPedido>("Pedido", n => n.Pedido?.CodigoPedido ?? "-", 85, Fija: true),
                        new("Tipo", n => n.TipoNovedad, 1.5f),
                        new("Registrada", n => n.FechaRegistro.ToString("dd/MM/yyyy HH:mm"), 85, Fija: true),
                        new("Por", n => n.RegistradoPor, 1.5f),
                        new("Descripción", n => n.Descripcion, 3)));
                });
            }));

        public byte[] GenerarReporteSolicitudesPdf(DatosSolicitudes datos, PeriodoReporte periodo)
        {
            var t = datos.Totales;
            return Pdf("Reporte de Solicitudes y Aprobaciones", periodo.Descripcion, "Todos los tipos", c => c.Column(col =>
            {
                col.Spacing(14);
                col.Item().Element(x => FilaMetricas(x, "Resumen del periodo",
                    "Solicitudes enviadas en el periodo. Tiempo de respuesta: desde que se envió hasta que se aprobó o rechazó.",
                    new("Solicitudes", t.Total.ToString(), ColorTotal),
                    new("Aprobadas", t.Aprobadas.ToString(), ColorCompletado),
                    new("Rechazadas", t.Rechazadas.ToString(), ColorCancelado),
                    new("Pendientes", t.Pendientes.ToString(), ColorPendiente),
                    new("Aprobación", t.PorcentajeAprobacion is double p ? $"{p:0}%" : "-", ColorCompletado),
                    new("Tiempo de respuesta", Horas(t.HorasPromedioRespuesta), AzulMarca)));

                void Grupo(string titulo, string nota, string columna, List<FilaSolicitudes> filas) =>
                    col.Item().Column(sec =>
                    {
                        sec.Item().EnsureSpace(90).Element(x => TituloSeccion(x, titulo, nota));
                        sec.Item().PaddingTop(6).Element(x => Tabla(x, filas, "No hay solicitudes en este periodo.",
                            new Columna<FilaSolicitudes>(columna, f => f.Grupo, 3),
                            new("Total", f => f.Total.ToString(), 1, Derecha: true),
                            new("Aprobadas", f => f.Aprobadas.ToString(), 1.2f, Derecha: true),
                            new("Rechazadas", f => f.Rechazadas.ToString(), 1.2f, Derecha: true),
                            new("Pendientes", f => f.Pendientes.ToString(), 1.2f, Derecha: true),
                            new("Aprobación", f => f.PorcentajeAprobacion is double p ? $"{p:0}%" : "-", 1.2f, Derecha: true),
                            new("Respuesta", f => Horas(f.HorasPromedioRespuesta), 1.3f, Derecha: true)));
                    });

                Grupo("Por tipo de solicitud", "Qué se pide más y cuánto tarda en responderse.", "Tipo", datos.PorTipo);
                Grupo("Por revisor", "Quién respondió las solicitudes y en cuánto tiempo.", "Revisor", datos.PorRevisor);
            }));
        }

        public byte[] GenerarReporteProductosUbicacionesPdf(DatosProductosUbicaciones datos, PeriodoReporte periodo, string? filtro) =>
            Pdf("Reporte de Productos y Ubicaciones más Pedidos", periodo.Descripcion, filtro ?? "Todas las bodegas", c => c.Column(col =>
            {
                col.Spacing(14);
                col.Item().Element(t => FilaMetricas(t, "Resumen del periodo", "Pedidos cargados en el periodo (sin cancelados).",
                    new("Unidades", datos.Unidades.ToString(), ColorTotal),
                    new("Productos", datos.ProductosDistintos.ToString(), AzulMarca),
                    new("Ubicaciones visitadas", datos.UbicacionesVisitadas.ToString(), AzulMarca),
                    new("Producto top", datos.Productos.FirstOrDefault()?.Codigo ?? "-", ColorEnProceso)));

                col.Item().Column(sec =>
                {
                    sec.Item().Element(t => TituloSeccion(t, $"Productos más pedidos (top {DatosProductosUbicaciones.Top})", "Por unidades pedidas."));
                    sec.Item().PaddingTop(6).Element(t => Tabla(t, datos.Productos, "No hay pedidos en este periodo.",
                        new Columna<FilaProducto>("Código", f => f.Codigo, 85, Fija: true),
                        new("Producto", f => f.Nombre, 4),
                        new("Unidades", f => f.Unidades.ToString(), 1.2f, Derecha: true),
                        new("Pedidos", f => f.Pedidos.ToString(), 1.2f, Derecha: true),
                        new("% unidades", f => datos.Unidades > 0 ? $"{100.0 * f.Unidades / datos.Unidades:0.#}%" : "-", 1.3f, Derecha: true)));
                });

                col.Item().Column(sec =>
                {
                    sec.Item().EnsureSpace(90).Element(t => TituloSeccion(t, $"Ubicaciones más visitadas (top {DatosProductosUbicaciones.Top})",
                        "Visitas: pedidos que pasaron por la ubicación. Las más visitadas conviene tenerlas cerca del despacho."));
                    sec.Item().PaddingTop(6).Element(t => Tabla(t, datos.Ubicaciones, "No hay pedidos en este periodo.",
                        new Columna<FilaUbicacion>("Ubicación", f => f.Codigo, 85, Fija: true),
                        new("Bodega", f => f.Bodega, 2),
                        new("Producto", f => f.Producto, 4),
                        new("Visitas", f => f.Visitas.ToString(), 1.1f, Derecha: true),
                        new("Unidades", f => f.Unidades.ToString(), 1.2f, Derecha: true)));
                });
            }));

        public byte[] GenerarReporteCargaHorariaPdf(DatosCargaHoraria datos, PeriodoReporte periodo, string? filtro)
        {
            var pico = datos.PorHora.OrderByDescending(f => f.Cargados).FirstOrDefault(f => f.Cargados > 0);
            var diaPico = datos.PorDia.OrderByDescending(f => f.Cargados).FirstOrDefault(f => f.Cargados > 0);

            return Pdf("Reporte de Carga por Hora y Día", periodo.Descripcion, filtro ?? "Todas las bodegas", c => c.Column(col =>
            {
                col.Spacing(14);
                col.Item().Element(t => FilaMetricas(t, "Resumen del periodo",
                    "Pedidos cargados en el periodo: cargados por su hora de carga, completados por la hora en que se terminaron.",
                    new("Cargados", datos.Cargados.ToString(), ColorTotal),
                    new("Completados", datos.Completados.ToString(), ColorCompletado),
                    new("Hora pico", pico != null ? $"{pico.Hora:00}:00" : "-", ColorEnProceso),
                    new("Día pico", diaPico?.Dia ?? "-", ColorEnProceso)));

                col.Item().Column(sec =>
                {
                    sec.Item().Element(t => TituloSeccion(t, "Por hora del día",
                        "Empleados en turno: activos hoy cuyo horario cubre esa hora. Si llegan más pedidos de los que se completan, falta gente en esa franja."));
                    sec.Item().PaddingTop(6).Element(t => Tabla(t, datos.PorHora, "No hay pedidos en este periodo.",
                        new Columna<FilaHora>("Hora", f => $"{f.Hora:00}:00 – {(f.Hora + 1) % 24:00}:00", 2),
                        new("Cargados", f => f.Cargados.ToString(), 1.2f, Derecha: true),
                        new("Completados", f => f.Completados.ToString(), 1.4f, Derecha: true),
                        new("Empleados en turno", f => f.EmpleadosEnTurno.ToString(), 1.8f, Derecha: true)));
                });

                col.Item().Column(sec =>
                {
                    sec.Item().EnsureSpace(90).Element(t => TituloSeccion(t, "Por día de la semana", "Tiempo promedio de los pedidos completados ese día."));
                    sec.Item().PaddingTop(6).Element(t => Tabla(t, datos.PorDia, "No hay pedidos en este periodo.",
                        new Columna<FilaDia>("Día", f => f.Dia, 2),
                        new("Cargados", f => f.Cargados.ToString(), 1.2f, Derecha: true),
                        new("Completados", f => f.Completados.ToString(), 1.4f, Derecha: true),
                        new("Tiempo promedio", f => Minutos(f.PromedioMinutos), 1.8f, Derecha: true)));
                });
            }));
        }

        private static string Horas(double? horas) => horas switch
        {
            null => "-",
            < 1 => $"{horas.Value * 60:0} min",
            < 48 => $"{horas.Value:0.0} h",
            _ => $"{horas.Value / 24:0.0} días"
        };

        private static string Desviacion(double? porcentaje) => porcentaje switch
        {
            null => "-",
            >= 0 => $"+{porcentaje.Value:0}%",
            _ => $"−{-porcentaje.Value:0}%"
        };

        private static string Porcentaje(double? valor) => valor is double v ? $"{v:0}%" : "-";

        private const string AzulMarca = "#1C3A6B";

        // Colores de cada estado: los mismos de las tarjetas del dashboard
        private const string ColorTotal = "#4B5563";
        private const string ColorPendiente = "#3B71CA";
        private const string ColorEnProceso = "#EB7D26";
        private const string ColorCompletado = "#52A447";
        private const string ColorCancelado = "#DC2626";
        private const string ColorReasignado = "#1F2937";

        private sealed record Metrica(string Etiqueta, string Valor, string Color);

        // Fila de tarjetas con título: el resumen en cifras que va arriba de la tabla de cada reporte
        private static void FilaMetricas(IContainer container, string titulo, string nota, params Metrica[] metricas) =>
            container.ShowEntire().Column(sec =>
            {
                sec.Item().Element(t => TituloSeccion(t, titulo, nota));
                sec.Item().PaddingTop(6).Row(row =>
                {
                    row.Spacing(6);
                    foreach (var m in metricas) TarjetaMetrica(row.RelativeItem(), m.Etiqueta, m.Valor, m.Color);
                });
            });

        // Cuántos pedidos del periodo terminaron en cada estado
        private static void ResumenEstados(IContainer container, ResumenEstadosPedidos r) =>
            FilaMetricas(container, "Pedidos del periodo",
                "Pedidos cargados en el periodo. Reasignados: los que cambiaron de empleado; también cuentan en su estado actual.",
                new("Total", r.Total.ToString(), ColorTotal),
                new("Pendientes", r.Pendientes.ToString(), ColorPendiente),
                new("En proceso", r.EnProceso.ToString(), ColorEnProceso),
                new("Completados", r.Completados.ToString(), ColorCompletado),
                new("Cancelados", r.Cancelados.ToString(), ColorCancelado),
                new("Reasignados", r.Reasignados.ToString(), ColorReasignado));

        // Todos los reportes comparten la página (tamaño, márgenes, fuente), el encabezado con logo y el pie con el
        // número de página. Los horizontales llevan letra más pequeña para que quepan más columnas.
        private byte[] Pdf(string titulo, string periodo, string? filtro, Action<IContainer> contenido, bool horizontal = false) =>
            Document.Create(container => container.Page(page =>
            {
                page.Size(horizontal ? PageSizes.A4.Landscape() : PageSizes.A4);
                page.Margin(1.5f, Unit.Centimetre);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(x => x.FontSize(horizontal ? 9 : 10).FontFamily("Lato"));

                page.Header().Element(c => ComposeHeader(c, titulo, periodo, filtro));
                page.Content().PaddingVertical(10).Element(contenido);
                page.Footer().Element(ComposeFooter);
            })).GeneratePdf();

        // Una columna de tabla: ancho relativo, o en puntos si es Fija
        private sealed record Columna<T>(string Titulo, Func<T, string> Valor, float Ancho = 1, bool Fija = false, bool Derecha = false);

        // Tabla con encabezado azul y una fila por dato; sin datos, una fila con el mensaje
        private static void Tabla<T>(IContainer container, IReadOnlyCollection<T> filas, string mensajeVacio, params Columna<T>[] columnas) =>
            container.Table(table =>
            {
                table.ColumnsDefinition(definicion =>
                {
                    foreach (var columna in columnas)
                    {
                        if (columna.Fija) definicion.ConstantColumn(columna.Ancho);
                        else definicion.RelativeColumn(columna.Ancho);
                    }
                });

                table.Header(header =>
                {
                    foreach (var columna in columnas)
                        Alinear(header.Cell().Element(CellStyleHeader), columna.Derecha).Text(columna.Titulo);
                });

                foreach (var fila in filas)
                    foreach (var columna in columnas)
                        Alinear(table.Cell().Element(CellStyleRow), columna.Derecha).Text(columna.Valor(fila));

                if (filas.Count == 0)
                {
                    table.Cell().ColumnSpan((uint)columnas.Length).Element(celda => FilaSinDatos(celda, mensajeVacio));
                }
            });

        private static IContainer Alinear(IContainer container, bool derecha) => derecha ? container.AlignRight() : container;

        private static string Minutos(double? minutos) => minutos.HasValue ? $"{minutos.Value:0.0} min" : "-";

        private static void TituloSeccion(IContainer container, string titulo, string subtitulo)
        {
            container.Column(c =>
            {
                c.Item().Text(titulo).FontSize(12).SemiBold().FontColor(AzulMarca);
                c.Item().Text(subtitulo).FontSize(8).FontColor(Colors.Grey.Darken1);
            });
        }

        // Tarjeta del resumen: cifra grande del color del estado, igual que las tarjetas del dashboard
        private static void TarjetaMetrica(IContainer container, string etiqueta, string valor, string color)
        {
            // Un texto largo ("11,4 min", "Inicio de Sesión (12)") va más pequeño para que no se parta en dos líneas
            container.Background("#F4F6F8").BorderTop(3).BorderColor(color).PaddingVertical(8).PaddingHorizontal(6).Column(c =>
            {
                c.Item().AlignCenter().Text(valor).FontSize(valor.Length > 10 ? 10 : valor.Length > 6 ? 15 : 20).Bold().FontColor(color);
                c.Item().AlignCenter().Text(etiqueta).FontSize(8).FontColor(Colors.Grey.Darken2);
            });
        }

        private void ComposeHeader(IContainer container, string titulo, string periodo, string? filtro)
        {
            container.Column(column =>
            {
                column.Item().Row(row =>
                {
                    // Logo + nombre de marca
                    row.ConstantItem(160).Row(brand =>
                    {
                        if (_logo != null)
                        {
                            brand.ConstantItem(52).Image(_logo).FitArea();
                        }
                        brand.RelativeItem().PaddingLeft(6).Column(txt =>
                        {
                            txt.Item().Text(t =>
                            {
                                t.Span("Pick").Bold().FontSize(18).FontColor(AzulMarca);
                                t.Span("flow").Bold().FontSize(18).FontColor("#E87722");
                            });
                            txt.Item().Text("Sistema de Gestión Logística")
                               .FontSize(7).FontColor(Colors.Grey.Medium);
                        });
                    });

                    // Título y periodo
                    row.RelativeItem().PaddingLeft(12).Column(col =>
                    {
                        col.Item().Text(titulo).FontSize(11).SemiBold().FontColor(AzulMarca);
                        col.Item().Text($"Periodo: {periodo}")
                           .FontSize(9).FontColor(Colors.Grey.Medium);
                        if (!string.IsNullOrWhiteSpace(filtro))
                        {
                            col.Item().Text(filtro).FontSize(9).SemiBold().FontColor(Colors.Grey.Darken2);
                        }
                    });

                    // Fecha de generación
                    row.ConstantItem(130).Column(col =>
                    {
                        col.Item().AlignRight().Text($"Generado: {DateTime.Now:dd/MM/yyyy HH:mm}")
                           .FontSize(8).FontColor(Colors.Grey.Medium);
                    });
                });

                column.Item().PaddingTop(6).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
            });
        }

        private static void ComposeFooter(IContainer container)
        {
            container.Row(row =>
            {
                row.RelativeItem().Text("PickFlow • Reporte exportado en formato PDF").FontSize(8).FontColor(Colors.Grey.Medium);
                row.ConstantItem(100).AlignRight().Text(x =>
                {
                    x.Span("Página ");
                    x.CurrentPageNumber();
                    x.Span(" de ");
                    x.TotalPages();
                });
            });
        }

        // Estilo de los encabezados de tabla: texto blanco en negrita sobre el azul de la marca
        private static IContainer CellStyleHeader(IContainer container)
        {
            return container.DefaultTextStyle(x => x.Bold().FontColor(Colors.White))
                            .PaddingVertical(6)
                            .PaddingHorizontal(4)
                            .Background(AzulMarca);
        }

        private static void FilaSinDatos(IContainer container, string mensaje)
        {
            container.PaddingVertical(14).AlignCenter().Text(mensaje).FontSize(9).Italic().FontColor(Colors.Grey.Darken1);
        }

        private static IContainer CellStyleRow(IContainer container)
        {
            return container.BorderBottom(1)
                            .BorderColor(Colors.Grey.Lighten2)
                            .PaddingVertical(5)
                            .PaddingHorizontal(4);
        }
    }
}
