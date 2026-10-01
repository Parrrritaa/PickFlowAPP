using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using PickingApp.Models;
using System.IO;

namespace PickingApp.Services
{
    // Arma los reportes en PDF con la librería QuestPDF. Solo dibuja: los datos ya llegan calculados
    // desde ReportesController, que devuelve los bytes al navegador como archivo descargable.
    public class ReportService : IReportService
    {
        public ReportService()
        {
            // Licencia gratuita de QuestPDF para proyectos pequeños y académicos
            QuestPDF.Settings.License = LicenseType.Community;
        }

        public byte[] GenerarReporteTiemposPorEmpleadoPdf(List<ReporteTiemposEmpleadoItem> datos, PeriodoReporte periodo)
        {
            // Todos los reportes siguen la misma estructura: página (tamaño, márgenes, fuente),
            // encabezado con logo, tabla con una fila por dato y pie con el número de página
            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(1.5f, Unit.Centimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(10).FontFamily("Lato"));

                    page.Header().Element(c => ComposeHeader(c, "Reporte de Tiempos de Picking por Empleado (CU-32)", periodo.Descripcion, "Todos los empleados"));

                    page.Content().PaddingVertical(10).Element(c =>
                    {
                        c.Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.ConstantColumn(80);
                                columns.RelativeColumn(3);
                                columns.RelativeColumn(1.5f);
                                columns.RelativeColumn(1.5f);
                                columns.RelativeColumn(1.5f);
                                columns.RelativeColumn(1.5f);
                            });

                            table.Header(header =>
                            {
                                header.Cell().Element(CellStyleHeader).Text("Doc ID");
                                header.Cell().Element(CellStyleHeader).Text("Empleado");
                                header.Cell().Element(CellStyleHeader).AlignRight().Text("Pedidos");
                                header.Cell().Element(CellStyleHeader).AlignRight().Text("Promedio (m)");
                                header.Cell().Element(CellStyleHeader).AlignRight().Text("Mínimo (m)");
                                header.Cell().Element(CellStyleHeader).AlignRight().Text("Máximo (m)");
                            });

                            // Una celda por columna, en el mismo orden de ColumnsDefinition
                            foreach (var item in datos)
                            {
                                table.Cell().Element(CellStyleRow).Text(item.Identificacion);
                                table.Cell().Element(CellStyleRow).Text(item.Empleado);
                                table.Cell().Element(CellStyleRow).AlignRight().Text(item.TotalPedidos.ToString());
                                table.Cell().Element(CellStyleRow).AlignRight().Text(item.PromedioMinutos.ToString("0.0"));
                                table.Cell().Element(CellStyleRow).AlignRight().Text(item.TiempoMinimo.ToString("0.0"));
                                table.Cell().Element(CellStyleRow).AlignRight().Text(item.TiempoMaximo.ToString("0.0"));
                            }

                            if (!datos.Any())
                            {
                                table.Cell().ColumnSpan(6).Element(celda => FilaSinDatos(celda, "No hay pedidos completados por empleados en este periodo."));
                            }
                        });
                    });

                    page.Footer().Element(ComposeFooter);
                });
            });

            // Devuelve el PDF como bytes; no se guarda en disco
            return document.GeneratePdf();
        }

        public byte[] GenerarReporteGeneralTiemposPdf(List<ReporteGeneralTiemposItem> datos, PeriodoReporte periodo)
        {
            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(1.5f, Unit.Centimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(10).FontFamily("Lato"));

                    page.Header().Element(c => ComposeHeader(c, "Reporte General de Tiempos de Entrega (CU-33)", periodo.Descripcion, "Todas las bodegas"));

                    page.Content().PaddingVertical(10).Element(c =>
                    {
                        c.Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(3);
                                columns.RelativeColumn(2);
                                columns.RelativeColumn(2);
                                columns.RelativeColumn(2);
                            });

                            table.Header(header =>
                            {
                                header.Cell().Element(CellStyleHeader).Text("Bodega");
                                header.Cell().Element(CellStyleHeader).AlignRight().Text("Pedidos Completados");
                                header.Cell().Element(CellStyleHeader).AlignRight().Text("Promedio (min)");
                                header.Cell().Element(CellStyleHeader).AlignRight().Text("Total Minutos");
                            });

                            foreach (var item in datos)
                            {
                                table.Cell().Element(CellStyleRow).Text(item.Bodega);
                                table.Cell().Element(CellStyleRow).AlignRight().Text(item.TotalCompletados.ToString());
                                table.Cell().Element(CellStyleRow).AlignRight().Text(item.PromedioMinutos.ToString("0.0"));
                                table.Cell().Element(CellStyleRow).AlignRight().Text(item.TiempoTotalMinutos.ToString("0.0"));
                            }

                            if (!datos.Any())
                            {
                                table.Cell().ColumnSpan(4).Element(celda => FilaSinDatos(celda, "No hay pedidos completados en este periodo."));
                            }
                        });
                    });

                    page.Footer().Element(ComposeFooter);
                });
            });

            return document.GeneratePdf();
        }

        public byte[] GenerarReportePedidosPorPeriodoPdf(List<Pedido> pedidos, PeriodoReporte periodo, string? filtro)
        {
            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4.Landscape());
                    page.Margin(1.5f, Unit.Centimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(9).FontFamily("Lato"));

                    page.Header().Element(c => ComposeHeader(c, "Reporte Consolidado de Pedidos por Periodo (CU-34)", periodo.Descripcion, filtro ?? "Todas las bodegas"));

                    page.Content().PaddingVertical(10).Element(c =>
                    {
                        c.Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.ConstantColumn(90);
                                columns.RelativeColumn(2);
                                columns.ConstantColumn(60);
                                columns.ConstantColumn(80);
                                columns.RelativeColumn(2);
                                columns.ConstantColumn(100);
                                columns.ConstantColumn(70);
                            });

                            table.Header(header =>
                            {
                                header.Cell().Element(CellStyleHeader).Text("Código");
                                header.Cell().Element(CellStyleHeader).Text("Bodega");
                                header.Cell().Element(CellStyleHeader).Text("Prioridad");
                                header.Cell().Element(CellStyleHeader).Text("Estado");
                                header.Cell().Element(CellStyleHeader).Text("Empleado");
                                header.Cell().Element(CellStyleHeader).Text("Fecha Carga");
                                header.Cell().Element(CellStyleHeader).AlignRight().Text("Tiempo (m)");
                            });

                            foreach (var p in pedidos)
                            {
                                table.Cell().Element(CellStyleRow).Text(p.CodigoPedido);
                                table.Cell().Element(CellStyleRow).Text(p.Bodega?.Nombre ?? "N/A");
                                table.Cell().Element(CellStyleRow).Text(p.Prioridad);
                                table.Cell().Element(CellStyleRow).Text(p.Estado);
                                table.Cell().Element(CellStyleRow).Text(p.Empleado?.NombreCompleto ?? "Sin asignar");
                                table.Cell().Element(CellStyleRow).Text(p.FechaCarga.ToString("dd/MM/yyyy HH:mm"));
                                table.Cell().Element(CellStyleRow).AlignRight().Text(p.TiempoProcesamientoMinutos?.ToString("0.0") ?? "-");
                            }

                            if (!pedidos.Any())
                            {
                                table.Cell().ColumnSpan(7).Element(celda => FilaSinDatos(celda, "No hay pedidos en este periodo."));
                            }
                        });
                    });

                    page.Footer().Element(ComposeFooter);
                });
            });

            return document.GeneratePdf();
        }

        public byte[] GenerarReporteCompletadosCanceladosReasignadosPdf(List<Pedido> pedidos, PeriodoReporte periodo, string? filtro)
        {
            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4.Landscape());
                    page.Margin(1.5f, Unit.Centimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(9).FontFamily("Lato"));

                    page.Header().Element(c => ComposeHeader(c, "Reporte de Pedidos Completados, Cancelados y Reasignados (CU-35)", periodo.Descripcion, filtro ?? "Completados, cancelados y reasignados"));

                    page.Content().PaddingVertical(10).Element(c =>
                    {
                        c.Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.ConstantColumn(90);
                                columns.ConstantColumn(80);
                                columns.RelativeColumn(2);
                                columns.ConstantColumn(100);
                                columns.ConstantColumn(70);
                                columns.RelativeColumn(3);
                            });

                            table.Header(header =>
                            {
                                header.Cell().Element(CellStyleHeader).Text("Código");
                                header.Cell().Element(CellStyleHeader).Text("Estado");
                                header.Cell().Element(CellStyleHeader).Text("Empleado");
                                header.Cell().Element(CellStyleHeader).Text("Fecha Fin");
                                header.Cell().Element(CellStyleHeader).AlignRight().Text("Tiempo");
                                header.Cell().Element(CellStyleHeader).Text("Motivo / Observación");
                            });

                            foreach (var p in pedidos)
                            {
                                var motivo = !string.IsNullOrEmpty(p.MotivoCancelacion) ? $"Cancelación: {p.MotivoCancelacion}" :
                                             (!string.IsNullOrEmpty(p.MotivoReasignacion) ? $"Reasignación: {p.MotivoReasignacion}" : 
                                             (p.ConfirmacionFisica ? "Entrega física confirmada" : "-"));

                                table.Cell().Element(CellStyleRow).Text(p.CodigoPedido);
                                table.Cell().Element(CellStyleRow).Text(p.Estado);
                                table.Cell().Element(CellStyleRow).Text(p.Empleado?.NombreCompleto ?? "-");
                                table.Cell().Element(CellStyleRow).Text(p.FechaFinalizacion?.ToString("dd/MM/yyyy HH:mm") ?? "-");
                                table.Cell().Element(CellStyleRow).AlignRight().Text(p.TiempoProcesamientoMinutos.HasValue ? $"{p.TiempoProcesamientoMinutos.Value:0.0} min" : "-");
                                table.Cell().Element(CellStyleRow).Text(motivo);
                            }

                            if (!pedidos.Any())
                            {
                                table.Cell().ColumnSpan(6).Element(celda => FilaSinDatos(celda, "No hay pedidos con estos estados en este periodo."));
                            }
                        });
                    });

                    page.Footer().Element(ComposeFooter);
                });
            });

            return document.GeneratePdf();
        }

        public byte[] GenerarReporteDetalleTiemposPdf(string titulo, string sujeto, List<Pedido> pedidos, PeriodoReporte periodo, bool porEmpleado)
        {
            var tiempos = pedidos.Where(p => p.TiempoProcesamientoMinutos.HasValue)
                                 .Select(p => p.TiempoProcesamientoMinutos!.Value)
                                 .ToList();
            var unidades = pedidos.Sum(p => p.Detalles.Sum(d => d.Cantidad));

            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(1.5f, Unit.Centimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(10).FontFamily("Lato"));

                    page.Header().Element(c => ComposeHeader(c, titulo, periodo.Descripcion, sujeto));

                    page.Content().PaddingVertical(10).Column(col =>
                    {
                        // Resumen en tarjetas
                        col.Item().PaddingBottom(12).Row(row =>
                        {
                            row.Spacing(8);

                            void Dato(string etiqueta, string valor) =>
                                row.RelativeItem().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(8).Column(d =>
                                {
                                    d.Item().Text(etiqueta).FontSize(8).FontColor(Colors.Grey.Darken1);
                                    d.Item().Text(valor).FontSize(13).Bold().FontColor("#1C3A6B");
                                });

                            Dato("Pedidos completados", pedidos.Count.ToString());
                            Dato("Unidades", unidades.ToString());
                            Dato("Promedio", tiempos.Any() ? $"{tiempos.Average():0.0} min" : "-");
                            Dato("Mínimo", tiempos.Any() ? $"{tiempos.Min():0.0} min" : "-");
                            Dato("Máximo", tiempos.Any() ? $"{tiempos.Max():0.0} min" : "-");
                        });

                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.ConstantColumn(90);
                                columns.RelativeColumn(3);
                                columns.ConstantColumn(95);
                                columns.ConstantColumn(55);
                                columns.ConstantColumn(65);
                                columns.ConstantColumn(60);
                            });

                            table.Header(header =>
                            {
                                header.Cell().Element(CellStyleHeader).Text("Pedido");
                                header.Cell().Element(CellStyleHeader).Text(porEmpleado ? "Bodega" : "Empleado");
                                header.Cell().Element(CellStyleHeader).Text("Finalizado");
                                header.Cell().Element(CellStyleHeader).AlignRight().Text("Unidades");
                                header.Cell().Element(CellStyleHeader).AlignRight().Text("Ubicaciones");
                                header.Cell().Element(CellStyleHeader).AlignRight().Text("Tiempo");
                            });

                            foreach (var p in pedidos)
                            {
                                var columna = porEmpleado
                                    ? (p.Bodega != null ? $"{p.Bodega.Nombre} ({p.Bodega.Codigo})" : "Sin bodega")
                                    : (p.Empleado?.NombreCompleto ?? "Sin asignar");

                                table.Cell().Element(CellStyleRow).Text(p.CodigoPedido);
                                table.Cell().Element(CellStyleRow).Text(columna);
                                table.Cell().Element(CellStyleRow).Text(p.FechaFinalizacion?.ToString("dd/MM/yyyy HH:mm") ?? "-");
                                table.Cell().Element(CellStyleRow).AlignRight().Text(p.Detalles.Sum(d => d.Cantidad).ToString());
                                table.Cell().Element(CellStyleRow).AlignRight().Text(p.Detalles.Select(d => d.UbicacionId).Distinct().Count().ToString());
                                table.Cell().Element(CellStyleRow).AlignRight().Text(p.TiempoProcesamientoMinutos.HasValue ? $"{p.TiempoProcesamientoMinutos.Value:0.0} min" : "-");
                            }

                            if (!pedidos.Any())
                            {
                                table.Cell().ColumnSpan(6).Element(celda => FilaSinDatos(celda, "No hay pedidos completados en este periodo."));
                            }
                        });
                    });

                    page.Footer().Element(ComposeFooter);
                });
            });

            return document.GeneratePdf();
        }

        public byte[] GenerarReporteLogSistemaPdf(List<LogSistema> logs, string? modulo, string? usuario, DateTime fechaDesde, DateTime fechaHasta)
        {
            var filtros = new List<string>();
            if (!string.IsNullOrWhiteSpace(modulo)) filtros.Add($"Módulo: {modulo}");
            if (!string.IsNullOrWhiteSpace(usuario)) filtros.Add($"Usuario: {usuario}");
            filtros.Add($"{logs.Count} registro(s)");
            var filtrosTexto = filtros.Any() ? string.Join(" • ", filtros) : "Sin filtros aplicados";

            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4.Landscape());
                    page.Margin(1.5f, Unit.Centimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(9).FontFamily("Lato"));

                    page.Header().Element(c => ComposeHeader(c, "LOG del Sistema (CU-38)", new PeriodoReporte(fechaDesde, fechaHasta).Descripcion, null));

                    page.Content().PaddingVertical(10).Column(col =>
                    {
                        col.Item().PaddingBottom(6).Text(filtrosTexto).FontSize(8).FontColor(Colors.Grey.Medium);

                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.ConstantColumn(90);
                                columns.RelativeColumn(2);
                                columns.RelativeColumn(2);
                                columns.RelativeColumn(4);
                            });

                            table.Header(header =>
                            {
                                header.Cell().Element(CellStyleHeader).Text("Fecha");
                                header.Cell().Element(CellStyleHeader).Text("Usuario");
                                header.Cell().Element(CellStyleHeader).Text("Acción");
                                header.Cell().Element(CellStyleHeader).Text("Descripción");
                            });

                            foreach (var l in logs)
                            {
                                table.Cell().Element(CellStyleRow).Text(l.FechaHora.ToString("dd/MM/yyyy HH:mm"));
                                table.Cell().Element(CellStyleRow).Text(l.UsuarioParaMostrar);
                                table.Cell().Element(CellStyleRow).Text(l.Accion);
                                table.Cell().Element(CellStyleRow).Text(l.Detalle);
                            }
                        });
                    });

                    page.Footer().Element(ComposeFooter);
                });
            });

            return document.GeneratePdf();
        }

        public byte[] GenerarReporteDashboardPdf(DashboardPdfDatos datos)
        {
            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(1.5f, Unit.Centimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(10).FontFamily("Lato"));

                    page.Header().Element(c => ComposeHeader(c, "Dashboard de Pedidos", datos.Periodo, datos.Filtros));

                    page.Content().PaddingVertical(10).Column(col =>
                    {
                        col.Spacing(14);

                        // 1) Tarjetas: totales generales, no dependen de los filtros de la gráfica
                        col.Item().ShowEntire().Column(sec =>
                        {
                            sec.Item().Element(c => TituloSeccion(c, "Resumen general", "Totales actuales de los pedidos de trabajo (un pedido dividido cuenta por sus partes)."));
                            sec.Item().PaddingTop(6).Row(row =>
                            {
                                row.Spacing(6);
                                TarjetaMetrica(row.RelativeItem(), "Total", datos.Total, "#4B5563");
                                TarjetaMetrica(row.RelativeItem(), "Pendientes", datos.Pendientes, "#3B71CA");
                                TarjetaMetrica(row.RelativeItem(), "En proceso", datos.EnProceso, "#EB7D26");
                                TarjetaMetrica(row.RelativeItem(), "Completados", datos.Completados, "#52A447");
                                TarjetaMetrica(row.RelativeItem(), "Cancelados", datos.Cancelados, "#DC2626");
                                TarjetaMetrica(row.RelativeItem(), "Reasignados", datos.Reasignados, "#1F2937");
                            });
                        });

                        // 2) Gráfica con los filtros que tenía el usuario en pantalla
                        col.Item().Element(c => TituloSeccion(c, datos.TituloGrafica,
                            $"Gráfica: {datos.TipoGrafica} · {datos.TotalGrafica} {(datos.TotalGrafica == 1 ? "pedido" : "pedidos")} con los filtros aplicados."));

                        if (datos.TotalGrafica == 0)
                        {
                            col.Item().Element(c => FilaSinDatos(c, "No hay pedidos con los filtros seleccionados."));
                        }
                        else
                        {
                            if (datos.ImagenGrafica != null)
                            {
                                col.Item().ShowEntire().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(6)
                                   .Height(235).AlignCenter().AlignMiddle().Image(datos.ImagenGrafica).FitArea();
                            }

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
                        col.Item().EnsureSpace(120).Element(c => TituloSeccion(c, "Pedidos recientes", "Los últimos pedidos cargados."));
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(2);
                                columns.RelativeColumn(1.2f);
                                columns.RelativeColumn(1.5f);
                                columns.RelativeColumn(2.4f);
                                columns.RelativeColumn(2);
                                columns.RelativeColumn(1.7f);
                            });

                            table.Header(header =>
                            {
                                header.Cell().Element(CellStyleHeader).Text("Pedido");
                                header.Cell().Element(CellStyleHeader).Text("Prioridad");
                                header.Cell().Element(CellStyleHeader).Text("Estado");
                                header.Cell().Element(CellStyleHeader).Text("Empleado");
                                header.Cell().Element(CellStyleHeader).Text("Bodega");
                                header.Cell().Element(CellStyleHeader).Text("Cargado");
                            });

                            foreach (var p in datos.Recientes)
                            {
                                table.Cell().Element(CellStyleRow).Text(p.CodigoPedido);
                                table.Cell().Element(CellStyleRow).Text(p.Prioridad);
                                table.Cell().Element(CellStyleRow).Text(p.Estado);
                                table.Cell().Element(CellStyleRow).Text(p.Empleado?.NombreCompleto ?? "Sin asignar");
                                table.Cell().Element(CellStyleRow).Text(p.Bodega?.Nombre ?? "—");
                                table.Cell().Element(CellStyleRow).Text(p.FechaCarga.ToString("dd/MM/yyyy HH:mm"));
                            }

                            if (!datos.Recientes.Any())
                            {
                                table.Cell().ColumnSpan(6).Element(celda => FilaSinDatos(celda, "No hay pedidos registrados."));
                            }
                        });
                    });

                    page.Footer().Element(ComposeFooter);
                });
            });

            return document.GeneratePdf();
        }

        private static void TituloSeccion(IContainer container, string titulo, string subtitulo)
        {
            container.Column(c =>
            {
                c.Item().Text(titulo).FontSize(12).SemiBold().FontColor("#1C3A6B");
                c.Item().Text(subtitulo).FontSize(8).FontColor(Colors.Grey.Darken1);
            });
        }

        // Tarjeta del resumen: cifra grande del color del estado, igual que las tarjetas del dashboard
        private static void TarjetaMetrica(IContainer container, string etiqueta, int valor, string color)
        {
            container.Background("#F4F6F8").BorderTop(3).BorderColor(color).PaddingVertical(8).PaddingHorizontal(6).Column(c =>
            {
                c.Item().AlignCenter().Text(valor.ToString()).FontSize(20).Bold().FontColor(color);
                c.Item().AlignCenter().Text(etiqueta).FontSize(8).FontColor(Colors.Grey.Darken2);
            });
        }

        private static void ComposeHeader(IContainer container, string titulo, string periodo, string? filtro)
        {
            // Ruta absoluta al logo en wwwroot
            var logoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wwwroot", "images", "pickflow-logo.jpg");

            container.Column(column =>
            {
                column.Item().Row(row =>
                {
                    // Logo + nombre de marca
                    row.ConstantItem(160).Row(brand =>
                    {
                        if (File.Exists(logoPath))
                        {
                            brand.ConstantItem(52).Image(logoPath).FitArea();
                        }
                        brand.RelativeItem().PaddingLeft(6).Column(txt =>
                        {
                            txt.Item().Text(t =>
                            {
                                t.Span("Pick").Bold().FontSize(18).FontColor("#1C3A6B");
                                t.Span("flow").Bold().FontSize(18).FontColor("#E87722");
                            });
                            txt.Item().Text("Sistema de Gestión Logística")
                               .FontSize(7).FontColor(Colors.Grey.Medium);
                        });
                    });

                    // Título y periodo
                    row.RelativeItem().PaddingLeft(12).Column(col =>
                    {
                        col.Item().Text(titulo).FontSize(11).SemiBold().FontColor("#1C3A6B");
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
                        col.Item().AlignRight().Text("Documento Oficial")
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
                row.RelativeItem().Text("PickFlow • Reporte exportado en formato PDF conforme a CU-36").FontSize(8).FontColor(Colors.Grey.Medium);
                row.ConstantItem(100).AlignRight().Text(x =>
                {
                    x.Span("Página ");
                    x.CurrentPageNumber();
                    x.Span(" de ");
                    x.TotalPages();
                });
            });
        }

        // Estilo de los encabezados de tabla: texto blanco en negrita sobre azul
        private static IContainer CellStyleHeader(IContainer container)
        {
            return container.DefaultTextStyle(x => x.Bold().FontColor(Colors.White))
                            .PaddingVertical(6)
                            .PaddingHorizontal(4)
                            .Background("#0D6EFD");
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
