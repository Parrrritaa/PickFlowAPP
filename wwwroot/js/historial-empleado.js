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

    const grid = { color: 'rgba(31,41,55,0.08)' };

    new Chart(lienzo, {
        type: 'line',
        data: {
            labels: datos.map(d => d.fecha),
            datasets: [
                {
                    // El real es el dato principal: línea continua, más gruesa y con puntos
                    label: 'Real',
                    data: datos.map(d => d.real),
                    borderColor: '#3B71CA',
                    backgroundColor: '#3B71CA',
                    borderWidth: 2.5,
                    pointRadius: datos.length > 20 ? 2 : 3.5,
                    pointHoverRadius: 6,
                    tension: 0.25
                },
                {
                    // El estimado es la referencia: línea discontinua y gris, así no se distingue solo por el color
                    label: 'Estimado',
                    data: datos.map(d => d.estimado),
                    borderColor: '#6B7280',
                    backgroundColor: '#6B7280',
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
            animation: sinMovimiento ? false : { duration: 500, easing: 'easeOutQuart' },
            interaction: { mode: 'index', intersect: false },
            scales: {
                x: { ticks: { color: '#4B5563', maxRotation: 0, autoSkip: true }, grid: { display: false }, border: { color: 'rgba(31,41,55,0.2)' } },
                y: {
                    beginAtZero: true,
                    grid,
                    border: { display: false },
                    ticks: { color: '#4B5563', callback: v => v + ' min' }
                }
            },
            plugins: {
                legend: {
                    display: true,
                    position: 'top',
                    align: 'end',
                    labels: { color: '#4B5563', usePointStyle: true, pointStyle: 'line', boxWidth: 24, padding: 14, font: { size: 13 } }
                },
                tooltip: {
                    backgroundColor: '#1F2937', padding: 10, cornerRadius: 8,
                    callbacks: {
                        title: items => datos[items[0].dataIndex].pedido + ' · ' + datos[items[0].dataIndex].fecha,
                        label: ctx => ctx.parsed.y == null ? ' ' + ctx.dataset.label + ': sin estimación'
                            : ' ' + ctx.dataset.label + ': ' + duracion(ctx.parsed.y)
                    }
                }
            }
        }
    });
})();
