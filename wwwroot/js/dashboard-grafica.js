// Dashboard (Views/Home/Index.cshtml): franja de estados, gráfica configurable y descarga del PDF.
// La gráfica pide los datos a HomeController.DatosGrafica cada vez que cambia un filtro y los dibuja con Chart.js.
(function () {
    const raiz = document.getElementById('pfGrafica');
    if (!raiz || typeof Chart === 'undefined') return;

    // El color sigue a la categoría, no a su posición: los estados mantienen los colores de la franja de arriba
    const COLOR_ESTADO = { Pendiente: '#3B71CA', Asignado: '#EB7D26', Completado: '#52A447', Cancelado: '#DC2626', Reasignado: '#1F2937' };
    const CATEGORICA = ['#2a78d6', '#eb6834', '#1baf7a', '#eda100', '#e87ba4', '#008300', '#4a3aa7', '#e34948'];
    const COLOR_UNICO = '#3B71CA';
    const TITULOS = { estado: 'Pedidos por estado', prioridad: 'Pedidos por prioridad', bodega: 'Pedidos por bodega', empleado: 'Pedidos por empleado (top 8)', dia: 'Pedidos cargados por día' };
    const ESTADO_EN_FRASE = { Pendiente: 'pendientes', Asignado: 'en proceso', Completado: 'completados', Cancelado: 'cancelados', Reasignado: 'reasignados' };
    const CAMBIO_MS = 160; // fundido al cambiar de tipo de gráfica

    const el = id => document.getElementById(id);
    const sinMovimiento = window.matchMedia('(prefers-reduced-motion: reduce)');
    const kpis = [...document.querySelectorAll('.pf-kpi')];
    const estado = { tipo: 'doughnut', tabla: false, datos: null };
    let grafica = null;
    let dibujada = null; // tipo y agrupación de la gráfica en pantalla: si no cambian, se actualiza en el sitio
    let peticion = 0;

    const esCircular = tipo => tipo === 'doughnut' || tipo === 'pie';
    const pedidos = n => n === 1 ? '1 pedido' : n + ' pedidos';
    const porcentaje = (v, total) => total ? Math.round(v * 100 / total) : 0;

    function estadosVisibles() {
        return kpis.filter(k => k.getAttribute('aria-pressed') === 'true').map(k => k.dataset.estado);
    }

    function filtros() {
        const activos = estadosVisibles();
        const p = new URLSearchParams({ agrupar: el('pfAgrupar').value, dias: el('pfDias').value });
        if (el('pfBodega').value) p.set('bodegaId', el('pfBodega').value);
        if (el('pfPrioridad').value) p.set('prioridad', el('pfPrioridad').value);
        // Con los cinco visibles no se filtra; con ninguno se envía un valor que no coincide con nada
        if (activos.length < kpis.length) p.set('estados', activos.length ? activos.join(',') : '-');
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

    function dibujar() {
        const { datos } = estado;
        const agrupar = el('pfAgrupar').value;
        const cats = datos.categorias;
        const vacio = datos.total === 0;
        const circ = esCircular(estado.tipo);
        const cols = colores(cats, agrupar);

        // Resumen y, si hay estados ocultos desde la franja, cuántos y cómo volver a verlos
        const ocultos = kpis.length - estadosVisibles().length;
        el('pfResumen').textContent = resumen(datos, agrupar);
        if (ocultos > 0) {
            const nota = document.createElement('span');
            nota.textContent = '· ' + (ocultos === 1 ? '1 estado oculto' : ocultos + ' estados ocultos');
            const mostrar = document.createElement('button');
            mostrar.type = 'button';
            mostrar.className = 'pf-link-btn';
            mostrar.textContent = 'Mostrar todos';
            mostrar.addEventListener('click', () => {
                kpis.forEach(k => k.setAttribute('aria-pressed', 'true'));
                cargar();
            });
            el('pfResumen').append(nota, mostrar);
        }
        el('pfVacio').hidden = !vacio;
        el('pfVacio').textContent = datos.error ? 'No se pudieron cargar los datos.' : 'No hay pedidos con estos filtros.';
        el('pfVerTabla').hidden = vacio;
        el('pfVerTabla').textContent = estado.tabla ? 'Ver como gráfica' : 'Ver como tabla';
        el('pfTabla').hidden = vacio || !estado.tabla;
        raiz.querySelector('.pf-chart-canvas').hidden = vacio || estado.tabla;
        el('pfCuerpo').classList.toggle('con-leyenda', circ && !vacio && !estado.tabla);

        // Leyenda al lado de la dona o el pastel (con valor y porcentaje); en barras y líneas lo dicen los ejes
        const leyenda = el('pfLeyenda');
        leyenda.innerHTML = '';
        leyenda.hidden = !circ || vacio || estado.tabla;
        if (!leyenda.hidden) {
            const visibles = estadosVisibles();
            cats.forEach((c, i) => {
                // Un estado oculto desde la franja no se lista: su 0 % no es un dato, es un filtro
                if (agrupar === 'estado' && !visibles.includes(c.clave)) return;
                const it = document.createElement('div');
                it.className = 'pf-legend-item' + (c.valor === 0 ? ' is-zero' : '');
                it.style.animationDelay = (i * 35) + 'ms';
                it.innerHTML = '<i></i><b></b><span class="pf-legend-valor"></span><span class="pf-legend-pct"></span>';
                it.querySelector('i').style.background = cols[i];
                it.querySelector('b').textContent = c.etiqueta;
                it.querySelector('.pf-legend-valor').textContent = c.valor;
                it.querySelector('.pf-legend-pct').textContent = porcentaje(c.valor, datos.total) + ' %';
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
                const d = document.createElement('td'); d.className = 'text-end'; d.textContent = porcentaje(c.valor, datos.total) + '%';
                tr.append(a, b, d); tb.appendChild(tr);
            });
            t.appendChild(tb); tabla.appendChild(t);
        }

        el('pfCentro').innerHTML = estado.tipo === 'doughnut' && !vacio && !estado.tabla
            ? '<strong>' + datos.total + '</strong><span>' + (datos.total === 1 ? 'pedido' : 'pedidos') + '</span>' : '';

        if (vacio || estado.tabla) {
            if (grafica) { grafica.destroy(); grafica = null; dibujada = null; }
            return;
        }

        // Mismo tipo y misma agrupación: se cambian los datos y Chart.js anima de los valores viejos a los nuevos
        const clave = estado.tipo + '|' + agrupar;
        if (grafica && dibujada === clave) {
            const nueva = configGrafica(estado.tipo, datos, agrupar, false);
            grafica.data.labels = nueva.data.labels;
            Object.assign(grafica.data.datasets[0], nueva.data.datasets[0]);
            grafica.options.plugins.tooltip = nueva.options.plugins.tooltip;
            grafica.update();
            return;
        }

        // Otro tipo u otra agrupación: la gráfica vieja se desvanece y la nueva entra con su animación
        const lienzo = raiz.querySelector('.pf-chart-canvas');
        const crear = () => {
            if (grafica) grafica.destroy();
            grafica = new Chart(el('detallesChart'), configGrafica(estado.tipo, datos, agrupar, false));
            dibujada = clave;
            lienzo.classList.remove('is-cambiando');
        };
        if (grafica && !sinMovimiento.matches) {
            lienzo.classList.add('is-cambiando');
            clearTimeout(dibujar.espera);
            dibujar.espera = setTimeout(crear, CAMBIO_MS);
        } else {
            crear();
        }
    }

    // Configuración de Chart.js para un tipo de gráfica. Con exportar = true sirve para la imagen del PDF:
    // sin animación, leyenda dibujada en el propio lienzo y el total en el centro de la dona.
    function configGrafica(tipo, datos, agrupar, exportar) {
        const cats = datos.categorias;
        const circ = esCircular(tipo);
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

        const grid = { color: 'rgba(31,41,55,0.08)' };
        const ejes = { ticks: { color: '#4B5563', precision: 0 }, grid, border: { display: false }, beginAtZero: true };
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
                        ? { display: true, position: 'bottom', labels: { color: '#4B5563', usePointStyle: true, pointStyle: 'rectRounded', boxWidth: 10, padding: 14, font: { size: 13 } } }
                        : { display: false },
                    tooltip: {
                        backgroundColor: '#1F2937', padding: 10, cornerRadius: 8, displayColors: porPunto,
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

    // Descarga del PDF del dashboard: se dibuja la gráfica actual en un lienzo aparte (con su leyenda) y se envía
    // como imagen junto a los filtros; el servidor recalcula los números con esos mismos filtros.
    const btnPdf = el('pfExportarPdf');
    if (btnPdf) {
        const textoBtn = btnPdf.querySelector('span');
        const original = textoBtn.textContent;
        btnPdf.addEventListener('click', async () => {
            if (btnPdf.disabled) return;
            btnPdf.disabled = true;
            textoBtn.textContent = 'Generando…';
            let copia = null;
            try {
                const form = el('pfPdfForm');
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

    // Tipo de gráfica: el botón copia el icono y el nombre de la opción elegida en el menú
    const tipos = el('pfTipos');
    function elegirTipo(b) {
        estado.tipo = b.dataset.tipo;
        tipos.querySelectorAll('.pf-tipo').forEach(x => {
            x.classList.toggle('active', x === b);
            x.setAttribute('aria-pressed', x === b);
        });
        el('pfTipoIcono').innerHTML = b.querySelector('svg').outerHTML;
        el('pfTipoNombre').textContent = b.querySelector('span').textContent;
        el('pfTipoBtn').setAttribute('aria-label', 'Tipo de gráfica: ' + b.querySelector('span').textContent);
        if (estado.datos) dibujar();
    }
    tipos.addEventListener('click', e => {
        const b = e.target.closest('.pf-tipo');
        if (b) elegirTipo(b);
    });

    // Filtros del menú: el botón cuenta los que se apartan de su valor inicial
    const camposFiltro = ['pfDias', 'pfBodega', 'pfPrioridad'].map(el);
    function contarFiltros() {
        const n = camposFiltro.filter(c => c.selectedIndex > 0).length;
        const cuenta = el('pfFiltrosCuenta');
        cuenta.hidden = n === 0;
        cuenta.textContent = n;
        el('pfLimpiar').disabled = n === 0;
        el('pfFiltrosBtn').setAttribute('aria-label', n ? 'Filtros, ' + n + ' activos' : 'Filtros');
    }
    camposFiltro.forEach(c => c.addEventListener('change', () => { contarFiltros(); cargar(); }));
    el('pfLimpiar').addEventListener('click', () => {
        camposFiltro.forEach(c => { c.selectedIndex = 0; });
        contarFiltros();
        cargar();
    });

    // El selector del título mide lo que dice la opción elegida, así la flecha queda pegada al texto
    const medidor = document.createElement('canvas').getContext('2d');
    function ajustarAgrupar() {
        const s = el('pfAgrupar');
        medidor.font = getComputedStyle(s).font;
        s.style.width = Math.ceil(medidor.measureText(s.options[s.selectedIndex].text).width) + 34 + 'px';
    }

    el('pfAgrupar').addEventListener('change', () => {
        ajustarAgrupar();
        // Por día solo tiene sentido como serie en el tiempo: se pasa a líneas si estaba en un gráfico circular
        if (el('pfAgrupar').value === 'dia' && esCircular(estado.tipo)) {
            elegirTipo(tipos.querySelector('[data-tipo="line"]'));
        }
        cargar();
    });

    // Franja de estados: cada uno muestra u oculta su estado en la gráfica
    el('pfEstados').addEventListener('click', e => {
        const k = e.target.closest('.pf-kpi');
        if (!k) return;
        k.setAttribute('aria-pressed', k.getAttribute('aria-pressed') !== 'true');
        cargar();
    });

    el('pfVerTabla').addEventListener('click', () => { estado.tabla = !estado.tabla; dibujar(); });

    // Los números de la franja cuentan desde cero al abrir el dashboard (una vez, y nunca con movimiento reducido)
    if (!sinMovimiento.matches) {
        const valores = kpis.map(k => k.querySelector('.pf-kpi-valor'));
        const finales = valores.map(v => Number(v.dataset.valor) || 0);
        const inicio = performance.now();
        const DURACION = 700;
        const paso = ahora => {
            const t = Math.min((ahora - inicio) / DURACION, 1);
            const suave = 1 - Math.pow(1 - t, 3);
            valores.forEach((v, i) => { v.textContent = Math.round(finales[i] * suave); });
            if (t < 1) requestAnimationFrame(paso);
        };
        requestAnimationFrame(paso);
    }

    ajustarAgrupar();
    document.fonts?.ready.then(ajustarAgrupar);
    elegirTipo(tipos.querySelector('.pf-tipo.active'));
    contarFiltros();
    cargar();
})();
