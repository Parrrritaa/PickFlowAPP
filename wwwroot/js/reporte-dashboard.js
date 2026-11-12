// Reporte general del dashboard (Views/Reportes/Index.cshtml, diálogo #modalReporteGeneral).
// Cada opción redibuja la vista previa con los datos de HomeController.DatosGrafica; al generar, la misma gráfica
// se dibuja como PNG y se envía con los filtros a HomeController.ExportarDashboardPdf, que recalcula las cifras.
(function () {
    const form = document.getElementById('formReporteGeneral');
    if (!form || typeof Chart === 'undefined' || !window.PfGrafica) return;

    const { TITULOS, configGrafica, esCircular, imagenPdf, resumen } = window.PfGrafica;
    const el = id => document.getElementById(id);
    const modal = form.closest('.modal');
    const btnGenerar = el('rgGenerar');
    const textoGenerar = btnGenerar.innerHTML;
    const estados = [...form.querySelectorAll('[data-estado]')];
    let grafica = null;
    let datos = null;
    let peticion = 0;

    const elegido = nombre => form.querySelector('input[name="' + nombre + '"]:checked').value;

    // Mismos parámetros que entiende DatosGrafica y ExportarDashboardPdf
    function filtros() {
        const p = new URLSearchParams({ agrupar: elegido('agrupar'), dias: el('rgDias').value });
        if (el('rgBodega').value) p.set('bodegaId', el('rgBodega').value);
        if (el('rgPrioridad').value) p.set('prioridad', el('rgPrioridad').value);
        const activos = estados.filter(e => e.checked).map(e => e.value);
        // Con todos marcados no se filtra; con ninguno se envía un valor que no coincide con nada
        if (activos.length < estados.length) p.set('estados', activos.length ? activos.join(',') : '-');
        return p;
    }

    async function cargar() {
        const mia = ++peticion;
        try {
            const r = await fetch(form.dataset.datos + '?' + filtros(), { headers: { 'Accept': 'application/json' } });
            if (!r.ok) throw new Error(r.status);
            const nuevos = await r.json();
            if (mia !== peticion) return; // llegó una respuesta más nueva
            datos = nuevos;
        } catch {
            if (mia !== peticion) return;
            datos = { total: 0, categorias: [], error: true };
        }
        dibujar();
    }

    function dibujar() {
        const tipo = elegido('tipo');
        const agrupar = elegido('agrupar');
        const vacio = datos.total === 0;

        el('rgVistaTitulo').textContent = TITULOS[agrupar];
        el('rgResumen').textContent = resumen(datos, agrupar);
        el('rgVacio').hidden = !vacio;
        el('rgVacio').textContent = datos.error ? 'No se pudieron cargar los datos.' : 'No hay pedidos con estos filtros.';
        form.querySelector('.pf-rg-lienzo').hidden = vacio;
        el('rgCentro').innerHTML = tipo === 'doughnut' && !vacio
            ? '<strong>' + datos.total + '</strong><span>' + (datos.total === 1 ? 'pedido' : 'pedidos') + '</span>' : '';

        if (grafica) { grafica.destroy(); grafica = null; }
        if (!vacio) grafica = new Chart(el('rgGrafica'), configGrafica(tipo, datos, agrupar, false));
    }

    form.addEventListener('change', e => {
        el('rgError').hidden = true;
        // Por día solo tiene sentido como serie en el tiempo: se pasa a líneas si estaba en un gráfico circular
        if (e.target.name === 'agrupar' && e.target.value === 'dia' && esCircular(elegido('tipo'))) {
            form.querySelector('input[name="tipo"][value="line"]').checked = true;
        }
        // El tipo de gráfica no cambia los datos: se redibuja sin volver a pedirlos
        if (e.target.name === 'tipo' && datos) dibujar();
        else void cargar();
    });

    // La vista previa se pide al abrir el diálogo, no al cargar la página
    modal.addEventListener('shown.bs.modal', () => { if (!datos) void cargar(); });

    form.addEventListener('submit', async e => {
        e.preventDefault();
        if (btnGenerar.disabled) return;
        btnGenerar.disabled = true;
        btnGenerar.classList.add('is-loading');
        btnGenerar.innerHTML = '<span class="pf-spinner" aria-hidden="true"></span>Generando…';
        el('rgError').hidden = true;
        try {
            if (!datos) await cargar();
            const f = filtros();
            f.set('tipo', elegido('tipo'));
            f.set('imagen', imagenPdf(elegido('tipo'), datos, elegido('agrupar')));
            f.set('__RequestVerificationToken', form.querySelector('input[name="__RequestVerificationToken"]').value);
            const r = await fetch(form.action, { method: 'POST', body: f });
            if (!r.ok) throw new Error(r.status);
            const blob = await r.blob();
            const m = /filename="?([^";]+)"?/i.exec(r.headers.get('Content-Disposition') || '');
            const url = URL.createObjectURL(blob);
            const a = document.createElement('a');
            a.href = url;
            a.download = m ? m[1] : 'Reporte_General_Dashboard.pdf';
            document.body.appendChild(a);
            a.click();
            a.remove();
            setTimeout(() => URL.revokeObjectURL(url), 10000);
            bootstrap.Modal.getOrCreateInstance(modal).hide();
        } catch {
            el('rgError').textContent = 'No se pudo generar el PDF. Intente de nuevo.';
            el('rgError').hidden = false;
        } finally {
            btnGenerar.disabled = false;
            btnGenerar.classList.remove('is-loading');
            btnGenerar.innerHTML = textoGenerar;
        }
    });
})();
