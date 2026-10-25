// Gráfica de la ficha de historial (Views/Empleados/Historial.cshtml): tiempo real y estimado de cada pedido
// completado, del más antiguo al más reciente. Mismo estilo de ejes y tooltip que las gráficas del Dashboard.
(function () {
    const fuente = document.getElementById('datosHistorial');
    const lienzo = document.getElementById('graficaHistorial');
    if (!fuente || !lienzo || typeof Chart === 'undefined') return;

    const datos = JSON.parse(fuente.textContent);
    const sinMovimiento = window.matchMedia('(prefers-reduced-motion: reduce)').matches;

    // Igual que Formato.Duracion en el servidor: "45 min", "2 h 15 min"
    function duracion(minutos) {
        if (minutos < 1) return 'menos de 1 min';
        const total = Math.round(minutos);
        if (total < 60) return total + ' min';
        const h = Math.floor(total / 60), m = total % 60;
        return m === 0 ? h + ' h' : h + ' h ' + m + ' min';
    }

    // Colores de la apariencia actual (tokens de site.css); al cambiarla la gráfica se vuelve a dibujar
    function colores() {
        const css = getComputedStyle(document.documentElement);
        const v = (nombre, porDefecto) => css.getPropertyValue(nombre).trim() || porDefecto;
        const oscuro = document.documentElement.getAttribute('data-theme') === 'dark';
        return {
            real: oscuro ? '#86AEF0' : '#3B71CA',
            estimado: v('--pf-ink3', '#6B7280'),
            texto: v('--pf-ink2', '#4B5563'),
            rejilla: v('--pf-line', 'rgba(31,41,55,0.08)'),
            eje: v('--pf-line2', 'rgba(31,41,55,0.2)'),
            tooltip: oscuro ? '#26364F' : '#0F1B2D'
        };
    }

    let grafica = null;
    function dibujar(animar) {
        const c = colores();
        const grid = { color: c.rejilla };
        const fuenteTexto = { family: "'Geist', system-ui, sans-serif", size: 12 };
        if (grafica) grafica.destroy();

        grafica = new Chart(lienzo, {
            type: 'line',
            data: {
                labels: datos.map(d => d.fecha),
                datasets: [
                    {
                        // El real es el dato principal: línea continua, más gruesa y con puntos
                        label: 'Real',
                        data: datos.map(d => d.real),
                        borderColor: c.real,
                        backgroundColor: c.real,
                        borderWidth: 2.5,
                        pointRadius: datos.length > 20 ? 2 : 3.5,
                        pointHoverRadius: 6,
                        tension: 0.25
                    },
                    {
                        // El estimado es la referencia: línea discontinua y gris, así no se distingue solo por el color
                        label: 'Estimado',
                        data: datos.map(d => d.estimado),
                        borderColor: c.estimado,
                        backgroundColor: c.estimado,
                        borderWidth: 2,
                        borderDash: [6, 5],
                        pointRadius: 0,
                        pointHoverRadius: 5,
                        tension: 0.25,
                        spanGaps: false
                    }
                ]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                animation: sinMovimiento || !animar ? false : { duration: 500, easing: 'easeOutQuart' },
                interaction: { mode: 'index', intersect: false },
                scales: {
                    x: { ticks: { color: c.texto, maxRotation: 0, autoSkip: true, font: fuenteTexto }, grid: { display: false }, border: { color: c.eje } },
                    y: {
                        beginAtZero: true,
                        grid,
                        border: { display: false },
                        ticks: { color: c.texto, font: fuenteTexto, callback: v => v + ' min' }
                    }
                },
                plugins: {
                    legend: {
                        display: true,
                        position: 'top',
                        align: 'end',
                        labels: { color: c.texto, usePointStyle: true, pointStyle: 'line', boxWidth: 24, padding: 14, font: { family: fuenteTexto.family, size: 13 } }
                    },
                    tooltip: {
                        backgroundColor: c.tooltip, padding: 10, cornerRadius: 10,
                        callbacks: {
                            title: items => datos[items[0].dataIndex].pedido + ' · ' + datos[items[0].dataIndex].fecha,
                            label: ctx => ctx.parsed.y == null ? ' ' + ctx.dataset.label + ': sin estimación'
                                : ' ' + ctx.dataset.label + ': ' + duracion(ctx.parsed.y)
                        }
                    }
                }
            }
        });
    }

    dibujar(true);
    document.addEventListener('pf-tema', () => dibujar(false));
})();
