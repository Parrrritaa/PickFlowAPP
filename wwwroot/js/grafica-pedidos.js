// Gráfica de pedidos compartida por el Dashboard (dashboard-grafica.js) y el reporte general de Reportes
// (reporte-dashboard.js): colores, frase de resumen y configuración de Chart.js para cada tipo de gráfica.
window.PfGrafica = (function () {
    // El color sigue a la categoría, no a su posición: los estados mantienen los colores de la franja del Dashboard
    const COLOR_ESTADO = { Pendiente: '#3B71CA', Asignado: '#EB7D26', Completado: '#52A447', Cancelado: '#DC2626', Reasignado: '#5F6673' };
    const CATEGORICA = ['#2a78d6', '#eb6834', '#1baf7a', '#eda100', '#e87ba4', '#008300', '#4a3aa7', '#e34948'];
    const COLOR_UNICO = '#3B71CA';

    // Colores de la gráfica según la apariencia (los tokens de site.css). La imagen del PDF va siempre en claro.
    function tinta(exportar) {
        const claro = { fondo: '#FFFFFF', texto: '#4B5563', rejilla: 'rgba(31,41,55,0.08)', eje: 'rgba(31,41,55,0.2)', tooltip: '#0F1B2D', neutro: '#5F6673', centro: '#1F2937', apoyo: '#6B7280' };
        if (exportar) return claro;
        const css = getComputedStyle(document.documentElement);
        const v = (nombre, porDefecto) => css.getPropertyValue(nombre).trim() || porDefecto;
        return {
            fondo: v('--pf-surface', claro.fondo),
            texto: v('--pf-ink2', claro.texto),
            rejilla: v('--pf-line', claro.rejilla),
            eje: v('--pf-line2', claro.eje),
            tooltip: document.documentElement.getAttribute('data-theme') === 'dark' ? '#26364F' : claro.tooltip,
            neutro: v('--pf-neutral-fill', claro.neutro)
        };
    }
    const TITULOS = { estado: 'Pedidos por estado', prioridad: 'Pedidos por prioridad', bodega: 'Pedidos por bodega', empleado: 'Pedidos por empleado (top 8)', dia: 'Pedidos cargados por día' };
    const ESTADO_EN_FRASE = { Pendiente: 'pendientes', Asignado: 'en proceso', Completado: 'completados', Cancelado: 'cancelados', Reasignado: 'reasignados' };

    const sinMovimiento = window.matchMedia('(prefers-reduced-motion: reduce)');
    const esCircular = tipo => tipo === 'doughnut' || tipo === 'pie';
    const pedidos = n => n === 1 ? '1 pedido' : n + ' pedidos';
    const porcentaje = (v, total) => total ? Math.round(v * 100 / total) : 0;

    function colores(cats, agrupar, exportar) {
        if (agrupar === 'estado') {
            const neutro = tinta(exportar).neutro;
            return cats.map(c => c.clave === 'Reasignado' ? neutro : COLOR_ESTADO[c.clave] || COLOR_UNICO);
        }
        return cats.map((_, i) => CATEGORICA[i % CATEGORICA.length]);
    }

    // Una frase con lo más importante de la gráfica, debajo del título
    function resumen(datos, agrupar) {
        if (datos.error) return 'No se pudieron cargar los datos.';
        if (datos.total === 0) return 'Sin pedidos con estos filtros';
        const mayor = datos.categorias.reduce((a, c) => c.valor > a.valor ? c : a, datos.categorias[0]);
        const pct = porcentaje(mayor.valor, datos.total);
        const frase = {
            estado: () => pct + ' % ' + ESTADO_EN_FRASE[mayor.clave],
            prioridad: () => pct + ' % de prioridad ' + mayor.etiqueta.toLowerCase(),
            bodega: () => pct + ' % en ' + mayor.etiqueta,
            empleado: () => 'más pedidos: ' + mayor.etiqueta + ' (' + mayor.valor + ')',
            dia: () => 'día con más carga: ' + mayor.etiqueta + ' (' + mayor.valor + ')'
        }[agrupar];
        return pedidos(datos.total) + ' · ' + frase();
    }

    // Configuración de Chart.js para un tipo de gráfica. Con exportar = true sirve para la imagen del PDF:
    // sin animación, leyenda dibujada en el propio lienzo y el total en el centro de la dona.
    function configGrafica(tipo, datos, agrupar, exportar) {
        const cats = datos.categorias;
        const circ = esCircular(tipo);
        const porPunto = circ || agrupar === 'estado';
        const cols = colores(cats, agrupar, exportar);
        const t = tinta(exportar);
        const horizontal = tipo === 'hbar';
        const linea = tipo === 'line' || tipo === 'area';
        const tipoChart = tipo === 'hbar' ? 'bar' : tipo === 'area' ? 'line' : tipo;
        const borde = t.fondo;

        const dataset = { label: TITULOS[agrupar], data: cats.map(c => c.valor) };
        if (circ) {
            dataset.backgroundColor = cols;
            dataset.borderColor = borde;
            dataset.borderWidth = 2;
            dataset.borderRadius = tipo === 'doughnut' ? 6 : 0;
            dataset.hoverOffset = exportar ? 0 : 8;
        } else if (linea) {
            dataset.borderColor = COLOR_UNICO;
            dataset.backgroundColor = tipo === 'area' ? 'rgba(59,113,202,0.22)' : COLOR_UNICO;
            dataset.fill = tipo === 'area';
            dataset.tension = 0.35;
            dataset.borderWidth = 2;
            dataset.pointRadius = cats.length > 31 ? 0 : 4;
            dataset.pointHoverRadius = 6;
            dataset.pointBackgroundColor = COLOR_UNICO;
            dataset.pointBorderColor = borde;
            dataset.pointBorderWidth = 2;
        } else {
            dataset.backgroundColor = porPunto ? cols : COLOR_UNICO;
            dataset.borderRadius = 6;
            dataset.borderSkipped = false;
            dataset.maxBarThickness = 44;
        }

        const grid = { color: t.rejilla };
        const fuente = { family: "'Geist', system-ui, sans-serif", size: 12 };
        const ejes = { ticks: { color: t.texto, precision: 0, font: fuente }, grid, border: { display: false }, beginAtZero: true };
        const ejeCat = { ticks: { color: t.texto, maxRotation: 0, autoSkip: true, font: fuente }, grid: { display: false }, border: { color: t.eje } };

        // Total en el centro de la dona: en pantalla es HTML superpuesto; en el PDF se dibuja en el lienzo
        const centroPdf = {
            id: 'centroPdf',
            afterDraw(chart) {
                if (!exportar || tipo !== 'doughnut') return;
                const { ctx, chartArea: ca } = chart;
                const x = (ca.left + ca.right) / 2, y = (ca.top + ca.bottom) / 2;
                ctx.save();
                ctx.textAlign = 'center';
                ctx.fillStyle = t.centro;
                ctx.font = '700 34px sans-serif';
                ctx.fillText(String(datos.total), x, y + 6);
                ctx.fillStyle = t.apoyo;
                ctx.font = '14px sans-serif';
                ctx.fillText('pedidos', x, y + 28);
                ctx.restore();
            }
        };

        // Entrada suave: la dona gira y crece desde el centro; las barras y líneas suben desde el eje
        const animacion = exportar || sinMovimiento.matches ? false
            : { duration: 650, easing: 'easeOutQuart', animateScale: circ, animateRotate: true };

        return {
            type: tipoChart,
            data: { labels: cats.map(c => c.etiqueta), datasets: [dataset] },
            plugins: exportar ? [centroPdf] : [],
            options: {
                responsive: !exportar,
                maintainAspectRatio: false,
                devicePixelRatio: exportar ? 2 : undefined,
                indexAxis: horizontal ? 'y' : 'x',
                cutout: tipo === 'doughnut' ? '68%' : undefined,
                animation: animacion,
                layout: { padding: circ ? 8 : 0 },
                interaction: circ ? undefined : { mode: 'index', intersect: false },
                scales: circ ? {} : (horizontal ? { x: ejes, y: ejeCat } : { x: ejeCat, y: ejes }),
                plugins: {
                    legend: exportar && porPunto
                        ? { display: true, position: 'bottom', labels: { color: t.texto, usePointStyle: true, pointStyle: 'rectRounded', boxWidth: 10, padding: 14, font: { size: 13 } } }
                        : { display: false },
                    tooltip: {
                        backgroundColor: t.tooltip, padding: 10, cornerRadius: 10, displayColors: porPunto,
                        titleFont: { family: "'Geist', system-ui, sans-serif", weight: '600' }, bodyFont: { family: "'Geist', system-ui, sans-serif" },
                        callbacks: {
                            label: ctx => {
                                const v = ctx.parsed.y ?? ctx.parsed.x ?? ctx.parsed;
                                return ' ' + pedidos(v) + ' (' + porcentaje(v, datos.total) + '%)';
                            }
                        }
                    }
                }
            }
        };
    }

    // Imagen PNG de la gráfica para el PDF: se dibuja en un lienzo aparte, sin animación y con su leyenda
    function imagenPdf(tipo, datos, agrupar) {
        if (!datos || datos.total === 0) return '';
        const lienzo = document.createElement('canvas');
        lienzo.width = 900;
        lienzo.height = 440;
        const copia = new Chart(lienzo, configGrafica(tipo, datos, agrupar, true));
        const imagen = lienzo.toDataURL('image/png');
        copia.destroy();
        return imagen;
    }

    return { TITULOS, colores, configGrafica, esCircular, imagenPdf, pedidos, porcentaje, resumen };
})();
