// Diálogos de reportes: el segmento "individual" revela su campo; los campos ocultos van deshabilitados
// (no se envían ni se validan). La descarga no cambia de página, así que el diálogo se cierra solo.
document.querySelectorAll('[data-reporte-form]').forEach(function (form) {
    var modalEl = form.closest('.modal');
    var btnGenerar = form.querySelector('[type="submit"]');
    var textoGenerar = btnGenerar.innerHTML;

    function actualizar(animar) {
        form.querySelectorAll('[data-si]').forEach(function (bloque) {
            var partes = bloque.dataset.si.split('=');
            var marcado = form.querySelector('input[name="' + partes[0] + '"]:checked');
            var visible = !!marcado && marcado.value === partes[1];
            if (visible === !bloque.hidden) return;

            bloque.hidden = !visible;
            bloque.querySelectorAll('input, select').forEach(function (c) { c.disabled = !visible; });
            if (visible && animar) {
                bloque.classList.remove('pf-appear');
                void bloque.offsetWidth;
                bloque.classList.add('pf-appear');
                var primero = bloque.querySelector('select, input');
                if (primero) primero.focus({ preventScroll: true });
            }
        });
    }

    function validarFechas() {
        var desde = form.querySelector('[data-fecha-desde]');
        var hasta = form.querySelector('[data-fecha-hasta]');
        if (!desde || !hasta) return;
        var invalido = !desde.disabled && desde.value && hasta.value && hasta.value < desde.value;
        hasta.setCustomValidity(invalido ? 'La fecha final debe ser igual o posterior a la inicial.' : '');
    }

    form.addEventListener('change', function (e) {
        if (e.target.classList.contains('pf-seg-input')) actualizar(true);
        validarFechas();
    });

    form.addEventListener('submit', function (e) {
        validarFechas();
        if (!form.checkValidity()) {
            e.preventDefault();
            form.reportValidity();
            return;
        }
        // Los segmentos solo controlan la interfaz: no viajan en la URL
        var segmentos = form.querySelectorAll('.pf-seg-input');
        segmentos.forEach(function (r) { r.disabled = true; });
        setTimeout(function () { segmentos.forEach(function (r) { r.disabled = false; }); }, 0);

        btnGenerar.classList.add('is-loading');
        btnGenerar.innerHTML = '<span class="pf-spinner" aria-hidden="true"></span>Generando…';
        setTimeout(function () { bootstrap.Modal.getOrCreateInstance(modalEl).hide(); }, 900);
    });

    modalEl.addEventListener('hidden.bs.modal', function () {
        btnGenerar.classList.remove('is-loading');
        btnGenerar.innerHTML = textoGenerar;
    });

    actualizar(false);
});
