// Gráfico configurable del dashboard (Views/Home/Index.cshtml). Pide los datos a HomeController.DatosGrafica
// cada vez que cambia un filtro y los dibuja con Chart.js en el tipo de gráfica elegido.
(function () {
    const raiz = document.getElementById('pfGrafica');
    if (!raiz || typeof Chart === 'undefined') return;

    // El color sigue a la categoría, no a su posición: los estados mantienen los colores de las tarjetas
    const COLOR_ESTADO = { Pendiente: '#3B71CA', Asignado: '#EB7D26', Completado: '#52A447', Cancelado: '#DC2626', Reasignado: '#1F2937' };
    const CATEGORICA = ['#2a78d6', '#eb6834', '#1baf7a', '#eda100', '#e87ba4', '#008300', '#4a3aa7', '#e34948'];
    const COLOR_UNICO = '#3B71CA';
    const TITULOS = { estado: 'Pedidos por estado', prioridad: 'Pedidos por prioridad', bodega: 'Pedidos por bodega', empleado: 'Pedidos por empleado (top 8)', dia: 'Pedidos cargados por día' };

    const el = id => document.getElementById(id);
    const estado = { tipo: 'doughnut', tabla: false, datos: null };
    let grafica = null;
    let peticion = 0;

    const esCircular = () => estado.tipo === 'doughnut' || estado.tipo === 'pie';

    function filtros() {
        const activos = [...raiz.querySelectorAll('.pf-chip.active')].map(c => c.dataset.estado);
        const p = new URLSearchParams({ agrupar: el('pfAgrupar').value, dias: el('pfDias').value });
        if (el('pfBodega').value) p.set('bodegaId', el('pfBodega').value);
        if (el('pfPrioridad').value) p.set('prioridad', el('pfPrioridad').value);
        // Con los cinco activos no se filtra; con ninguno se envía un valor que no coincide con nada
        if (activos.length < 5) p.set('estados', activos.length ? activos.join(',') : '-');
        return p;
    }

    async function cargar() {
        const mia = ++peticion;
        try {
            const r = await fetch(raiz.dataset.url + '?' + filtros(), { headers: { 'Accept': 'application/json' } });
            if (!r.ok) throw new Error(r.status);
            const datos = await r.json();
            if (mia !== peticion) return; // llegó una respuesta más nueva
            estado.datos = datos;
        } catch {
            if (mia !== peticion) return;
            estado.datos = { total: 0, categorias: [], error: true };
        }
        dibujar();
    }

    function colores(cats, agrupar) {
        if (agrupar === 'estado') return cats.map(c => COLOR_ESTADO[c.clave] || COLOR_UNICO);
        return cats.map((_, i) => CATEGORICA[i % CATEGORICA.length]);
    }

    function dibujar() {
        const { datos } = estado;
        const agrupar = el('pfAgrupar').value;
        const cats = datos.categorias;
        const vacio = datos.total === 0;

        el('pfVacio').hidden = !vacio;
        el('pfVacio').textContent = datos.error ? 'No se pudieron cargar los datos.' : 'No hay pedidos con estos filtros.';
        el('pfVerTabla').hidden = vacio;
        el('pfTabla').hidden = vacio || !estado.tabla;
        raiz.querySelector('.pf-chart-canvas').hidden = vacio || estado.tabla;
        el('pfVerTabla').textContent = estado.tabla ? 'Ver gráfica' : 'Ver tabla';

        const circ = esCircular();
        const porPunto = circ || agrupar === 'estado';
        const cols = colores(cats, agrupar);

        // Leyenda propia (con valor y porcentaje) solo cuando el color identifica la categoría
        const leyenda = el('pfLeyenda');
        leyenda.innerHTML = '';
        if (!vacio && (circ || agrupar === 'estado')) {
            cats.forEach((c, i) => {
                const it = document.createElement('span');
                it.className = 'pf-legend-item' + (c.valor === 0 ? ' is-zero' : '');
                const pct = datos.total ? Math.round(c.valor * 100 / datos.total) : 0;
                it.innerHTML = '<i></i><b></b><span></span>';
                it.querySelector('i').style.background = cols[i];
                it.querySelector('b').textContent = c.etiqueta;
                it.querySelector('span').textContent = c.valor + ' · ' + pct + '%';
                leyenda.appendChild(it);
            });
        }

        // Tabla accesible con los mismos datos
        const tabla = el('pfTabla');
        tabla.innerHTML = '';
        if (!vacio) {
            const t = document.createElement('table');
            t.className = 'mockup-table';
            t.innerHTML = '<thead><tr><th></th><th class="text-end">Pedidos</th><th class="text-end">%</th></tr></thead>';
            const tb = document.createElement('tbody');
            cats.forEach(c => {
                const tr = document.createElement('tr');
                const a = document.createElement('td'); a.textContent = c.etiqueta;
                const b = document.createElement('td'); b.className = 'text-end'; b.textContent = c.valor;
                const d = document.createElement('td'); d.className = 'text-end'; d.textContent = (datos.total ? Math.round(c.valor * 100 / datos.total) : 0) + '%';
                tr.append(a, b, d); tb.appendChild(tr);
            });
            t.appendChild(tb); tabla.appendChild(t);
        }

        el('pfCentro').innerHTML = estado.tipo === 'doughnut' && !vacio && !estado.tabla
            ? '<strong>' + datos.total + '</strong><span>pedidos</span>' : '';

        if (grafica) { grafica.destroy(); grafica = null; }
        if (vacio || estado.tabla) return;

        grafica = new Chart(el('detallesChart'), configGrafica(estado.tipo, datos, agrupar, false));
    }

    // Configuración de Chart.js para un tipo de gráfica. Con exportar = true sirve para la imagen del PDF:
    // sin animación, leyenda dibujada en el propio lienzo y el total en el centro de la dona.
    function configGrafica(tipo, datos, agrupar, exportar) {
        const cats = datos.categorias;
        const circ = tipo === 'doughnut' || tipo === 'pie';
        const porPunto = circ || agrupar === 'estado';
        const cols = colores(cats, agrupar);
        const horizontal = tipo === 'hbar';
        const linea = tipo === 'line' || tipo === 'area';
        const tipoChart = tipo === 'hbar' ? 'bar' : tipo === 'area' ? 'line' : tipo;
        const borde = exportar ? '#FFFFFF' : '#E6E9EC';

        const dataset = { label: TITULOS[agrupar], data: cats.map(c => c.valor) };
        if (circ) {
            dataset.backgroundColor = cols;
            dataset.borderColor = borde;
            dataset.borderWidth = 2;
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

        const grid = { color: 'rgba(31,41,55,0.08)' };
        const ejes = { ticks: { color: '#6B7280', precision: 0 }, grid, border: { display: false }, beginAtZero: true };
        const ejeCat = { ticks: { color: '#4B5563', maxRotation: 0, autoSkip: true }, grid: { display: false }, border: { color: 'rgba(31,41,55,0.2)' } };

        // Total en el centro de la dona: en pantalla es HTML superpuesto; en el PDF se dibuja en el lienzo
        const centroPdf = {
            id: 'centroPdf',
            afterDraw(chart) {
                if (!exportar || tipo !== 'doughnut') return;
                const { ctx, chartArea: ca } = chart;
                const x = (ca.left + ca.right) / 2, y = (ca.top + ca.bottom) / 2;
                ctx.save();
                ctx.textAlign = 'center';
                ctx.fillStyle = '#1F2937';
                ctx.font = '700 34px sans-serif';
                ctx.fillText(String(datos.total), x, y + 6);
                ctx.fillStyle = '#6B7280';
                ctx.font = '14px sans-serif';
                ctx.fillText('pedidos', x, y + 28);
                ctx.restore();
            }
        };

        return {
            type: tipoChart,
            data: { labels: cats.map(c => c.etiqueta), datasets: [dataset] },
            plugins: exportar ? [centroPdf] : [],
            options: {
                responsive: !exportar,
                maintainAspectRatio: false,
                devicePixelRatio: exportar ? 2 : undefined,
                indexAxis: horizontal ? 'y' : 'x',
                cutout: tipo === 'doughnut' ? '64%' : undefined,
                animation: exportar ? false : { duration: 450 },
                interaction: circ ? undefined : { mode: 'index', intersect: false },
                scales: circ ? {} : (horizontal ? { x: ejes, y: ejeCat } : { x: ejeCat, y: ejes }),
                plugins: {
                    legend: exportar && porPunto
                        ? { display: true, position: 'bottom', labels: { color: '#4B5563', usePointStyle: true, pointStyle: 'rectRounded', boxWidth: 10, padding: 14, font: { size: 13 } } }
                        : { display: false },
                    tooltip: {
                        backgroundColor: '#1F2937', padding: 10, cornerRadius: 8, displayColors: porPunto,
                        callbacks: {
                            label: ctx => {
                                const v = ctx.parsed.y ?? ctx.parsed.x ?? ctx.parsed;
                                const pct = datos.total ? Math.round(v * 100 / datos.total) : 0;
                                return ' ' + v + (v === 1 ? ' pedido' : ' pedidos') + ' (' + pct + '%)';
                            }
                        }
                    }
                }
            }
        };
    }

    // Descarga del PDF del dashboard: se dibuja la gráfica actual en un lienzo aparte (con su leyenda) y se envía
    // como imagen junto a los filtros; el servidor recalcula los números con esos mismos filtros.
    const btnPdf = document.getElementById('pfExportarPdf');
    if (btnPdf) {
        const textoBtn = btnPdf.querySelector('span');
        const original = textoBtn.textContent;
        btnPdf.addEventListener('click', async () => {
            if (btnPdf.disabled) return;
            btnPdf.disabled = true;
            textoBtn.textContent = 'Generando…';
            let copia = null;
            try {
                const form = document.getElementById('pfPdfForm');
                const datos = estado.datos;
                let imagen = '';
                if (datos && datos.total > 0) {
                    const lienzo = document.createElement('canvas');
                    lienzo.width = 900;
                    lienzo.height = 440;
                    copia = new Chart(lienzo, configGrafica(estado.tipo, datos, el('pfAgrupar').value, true));
                    imagen = lienzo.toDataURL('image/png');
                }
                const f = filtros();
                f.set('tipo', estado.tipo);
                f.set('imagen', imagen);
                f.set('__RequestVerificationToken', form.querySelector('input[name="__RequestVerificationToken"]').value);
                const r = await fetch(form.action, { method: 'POST', body: f });
                if (!r.ok) throw new Error(r.status);
                const blob = await r.blob();
                const m = /filename="?([^";]+)"?/i.exec(r.headers.get('Content-Disposition') || '');
                const url = URL.createObjectURL(blob);
                const a = document.createElement('a');
                a.href = url;
                a.download = m ? m[1] : 'Dashboard.pdf';
                document.body.appendChild(a);
                a.click();
                a.remove();
                setTimeout(() => URL.revokeObjectURL(url), 10000);
                textoBtn.textContent = 'Descargado';
            } catch {
                textoBtn.textContent = 'No se pudo generar';
            } finally {
                if (copia) copia.destroy();
                setTimeout(() => { textoBtn.textContent = original; btnPdf.disabled = false; }, 1800);
            }
        });
    }

    // Controles
    // La píldora del selector se coloca con la medida real del botón activo (sigue funcionando si cambia el tamaño)
    const selector = el('pfTipos');
    const thumb = document.createElement('span');
    thumb.className = 'pf-seg-thumb';
    thumb.setAttribute('aria-hidden', 'true');
    selector.prepend(thumb);

    function moverPildora() {
        const b = selector.querySelector('.pf-seg-btn.active');
        if (!b) return;
        thumb.style.width = b.offsetWidth + 'px';
        thumb.style.transform = 'translateX(' + b.offsetLeft + 'px)';
    }
    function elegirTipo(b) {
        estado.tipo = b.dataset.tipo;
        selector.querySelectorAll('.pf-seg-btn').forEach(x => {
            x.classList.toggle('active', x === b);
            x.setAttribute('aria-pressed', x === b);
        });
        moverPildora();
        dibujar();
    }
    selector.addEventListener('click', e => {
        const b = e.target.closest('.pf-seg-btn');
        if (b) elegirTipo(b);
    });
    // Flechas izquierda/derecha para recorrer los tipos con el teclado
    selector.addEventListener('keydown', e => {
        if (e.key !== 'ArrowLeft' && e.key !== 'ArrowRight') return;
        const bs = [...selector.querySelectorAll('.pf-seg-btn')];
        const i = bs.indexOf(document.activeElement);
        if (i < 0) return;
        const sig = bs[(i + (e.key === 'ArrowRight' ? 1 : bs.length - 1)) % bs.length];
        sig.focus();
        elegirTipo(sig);
        e.preventDefault();
    });
    moverPildora();
    requestAnimationFrame(() => requestAnimationFrame(() => selector.classList.add('is-ready'))); // sin animar la colocación inicial
    window.addEventListener('resize', moverPildora);
    el('pfEstados').addEventListener('click', e => {
        const c = e.target.closest('.pf-chip');
        if (!c) return;
        c.classList.toggle('active');
        c.setAttribute('aria-pressed', c.classList.contains('active'));
        cargar();
    });
    ['pfAgrupar', 'pfDias', 'pfBodega', 'pfPrioridad'].forEach(id => el(id).addEventListener('change', () => {
        // Por día solo tiene sentido como serie en el tiempo: se pasa a líneas si estaba en un gráfico circular
        if (id === 'pfAgrupar' && el('pfAgrupar').value === 'dia' && esCircular()) {
            elegirTipo(selector.querySelector('[data-tipo="line"]'));
        }
        cargar();
    }));
    el('pfVerTabla').addEventListener('click', () => { estado.tabla = !estado.tabla; dibujar(); });

    raiz.querySelectorAll('.pf-chip').forEach(c => c.setAttribute('aria-pressed', 'true'));
    cargar();
})();
