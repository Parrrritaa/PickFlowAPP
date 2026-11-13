// Dashboard (Views/Home/Index.cshtml): franja de estados y gráfica configurable.
// La gráfica pide los datos a HomeController.DatosGrafica cada vez que cambia un filtro y los dibuja con Chart.js;
// los colores y la configuración de cada tipo de gráfica están en grafica-pedidos.js (también los usa Reportes).
(function () {
    const raiz = document.getElementById('pfGrafica');
    if (!raiz || typeof Chart === 'undefined' || !window.PfGrafica) return;

    const { colores, configGrafica, esCircular, pedidos, porcentaje, resumen } = window.PfGrafica;
    const CAMBIO_MS = 160; // fundido al cambiar de tipo de gráfica

    const el = id => document.getElementById(id);
    const sinMovimiento = window.matchMedia('(prefers-reduced-motion: reduce)');
    const kpis = [...document.querySelectorAll('.pf-kpi')];
    const estado = { tipo: 'doughnut', tabla: false, datos: null };
    let grafica = null;
    let dibujada = null; // tipo y agrupación de la gráfica en pantalla: si no cambian, se actualiza en el sitio
    let peticion = 0;

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
                void cargar();
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
            t.className = 'pf-table';
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
        el('pfCentro').parentElement.setAttribute('aria-label', vacio ? 'Gráfica de pedidos' : 'Gráfica de pedidos: ' + datos.total + (datos.total === 1 ? ' pedido' : ' pedidos'));

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
    camposFiltro.forEach(c => c.addEventListener('change', () => { contarFiltros(); void cargar(); }));
    el('pfLimpiar').addEventListener('click', () => {
        camposFiltro.forEach(c => { c.selectedIndex = 0; });
        contarFiltros();
        void cargar();
    });

    // El título muestra la agrupación elegida en un botón con menú propio (el select oculto guarda el valor)
    function ajustarAgrupar() {
        const s = el('pfAgrupar');
        el('pfAgruparTexto').textContent = s.options[s.selectedIndex].text;
        el('pfAgruparMenu').querySelectorAll('.pf-menu-opcion').forEach(o => {
            o.classList.toggle('active', o.dataset.valor === s.value);
            o.setAttribute('aria-pressed', o.dataset.valor === s.value);
        });
    }

    el('pfAgruparMenu').addEventListener('click', e => {
        const o = e.target.closest('.pf-menu-opcion');
        if (!o) return;
        const s = el('pfAgrupar');
        if (s.value !== o.dataset.valor) {
            s.value = o.dataset.valor;
            s.dispatchEvent(new Event('change'));
        }
        bootstrap.Dropdown.getOrCreateInstance(el('pfAgruparBtn')).hide();
    });

    el('pfAgrupar').addEventListener('change', () => {
        ajustarAgrupar();
        // Por día solo tiene sentido como serie en el tiempo: se pasa a líneas si estaba en un gráfico circular
        if (el('pfAgrupar').value === 'dia' && esCircular(estado.tipo)) {
            elegirTipo(tipos.querySelector('[data-tipo="line"]'));
        }
        void cargar();
    });

    // Franja de estados: cada uno muestra u oculta su estado en la gráfica
    el('pfEstados').addEventListener('click', e => {
        const k = e.target.closest('.pf-kpi');
        if (!k) return;
        k.setAttribute('aria-pressed', k.getAttribute('aria-pressed') !== 'true');
        void cargar();
    });

    el('pfVerTabla').addEventListener('click', () => { estado.tabla = !estado.tabla; dibujar(); });

    // Al cambiar de apariencia la gráfica se vuelve a crear con los colores nuevos (sin animación de entrada)
    document.addEventListener('pf-tema', () => {
        if (!estado.datos) return;
        if (grafica) { grafica.destroy(); grafica = null; }
        dibujada = null;
        dibujar();
    });

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
    void document.fonts?.ready.then(ajustarAgrupar);
    elegirTipo(tipos.querySelector('.pf-tipo.active'));
    contarFiltros();
    void cargar();
})();
