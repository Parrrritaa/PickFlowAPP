(function () {
    var cfg = JSON.parse(document.getElementById('datosDivision').textContent);
    var lineas = cfg.lineas;
    var campoPartes = document.getElementById('numeroPartes');
    var encabezado = document.getElementById('filaEncabezado');
    var resumen = document.getElementById('resumenPartes');
    var mensaje = document.getElementById('mensajeReparto');
    var btnDividir = document.getElementById('btnDividir');
    var filas = {};
    document.querySelectorAll('#tablaReparto tbody tr[data-linea]').forEach(function (tr) { filas[tr.dataset.linea] = tr; });

    var partes = 0;
    var reparto = []; // reparto[i][k] = unidades de la línea i en la parte k

    // Mismo formato que Formato.Duracion en el servidor
    function duracion(min) {
        if (min < 1) return 'menos de 1 min';
        var total = Math.round(min);
        if (total < 60) return total + ' min';
        var h = Math.floor(total / 60), m = total % 60;
        if (h < 24) return m === 0 ? h + ' h' : h + ' h ' + m + ' min';
        var d = Math.floor(h / 24), hr = h % 24;
        var td = d === 1 ? '1 día' : d + ' días';
        return hr === 0 ? td : td + ' ' + hr + ' h';
    }

    // Tiempo estándar de una parte: unidades × minutos de su categoría + recorrido por ubicación
    function tiempoParte(k) {
        var minutos = 0, ubicaciones = {};
        lineas.forEach(function (l, i) {
            var u = reparto[i][k];
            if (u > 0) { minutos += u * l.minutos; ubicaciones[l.ubicacion] = true; }
        });
        return minutos + Object.keys(ubicaciones).length * cfg.minutosPorUbicacion;
    }

    function unidadesParte(k) {
        return lineas.reduce(function (s, l, i) { return s + reparto[i][k]; }, 0);
    }

    // Reparto automático: llena las partes en el orden de la ruta hasta el tiempo objetivo de cada una
    function repartir(n) {
        reparto = lineas.map(function () { var a = []; for (var k = 0; k < n; k++) a.push(0); return a; });
        var total = lineas.reduce(function (s, l) { return s + l.cantidad * l.minutos; }, 0)
            + new Set(lineas.map(function (l) { return l.ubicacion; })).size * cfg.minutosPorUbicacion;
        var objetivo = total / n;
        var k = 0, tiempo = 0, enParte = {};

        lineas.forEach(function (l, i) {
            var restantes = l.cantidad;
            while (restantes > 0) {
                if (k === n - 1) { reparto[i][k] += restantes; break; }
                var recorrido = enParte[l.ubicacion] ? 0 : cfg.minutosPorUbicacion;
                var caben = Math.round((objetivo - tiempo - recorrido) / l.minutos);
                if (caben <= 0) {
                    if (tiempo === 0) { caben = 1; }
                    else { k++; tiempo = 0; enParte = {}; continue; }
                }
                var poner = Math.min(caben, restantes);
                reparto[i][k] += poner;
                restantes -= poner;
                tiempo += poner * l.minutos + recorrido;
                enParte[l.ubicacion] = true;
            }
        });

        // Ninguna parte queda vacía: toma una unidad de la parte con más unidades
        for (var vacia = 0; vacia < n; vacia++) {
            if (unidadesParte(vacia) > 0) continue;
            var mejor = null;
            for (var p = 0; p < n; p++) {
                if (unidadesParte(p) < 2) continue;
                lineas.forEach(function (l, i) {
                    if (reparto[i][p] > 0 && (!mejor || reparto[i][p] > reparto[mejor.i][mejor.p])) mejor = { i: i, p: p };
                });
            }
            if (!mejor) break;
            reparto[mejor.i][mejor.p]--;
            reparto[mejor.i][vacia]++;
        }
    }

    function construirColumnas(n) {
        encabezado.querySelectorAll('[data-parte]').forEach(function (th) { th.remove(); });
        document.querySelectorAll('#tablaReparto td[data-parte]').forEach(function (td) { td.remove(); });
        var antesDe = encabezado.lastElementChild;
        for (var k = 0; k < n; k++) {
            var th = document.createElement('th');
            th.className = 'text-end';
            th.dataset.parte = k;
            th.textContent = 'Parte ' + (k + 1);
            encabezado.insertBefore(th, antesDe);
        }
        lineas.forEach(function (l, i) {
            var tr = filas[l.id];
            var ultima = tr.lastElementChild;
            for (var k = 0; k < n; k++) {
                var td = document.createElement('td');
                td.className = 'text-end';
                td.dataset.parte = k;
                var input = document.createElement('input');
                input.type = 'number';
                input.min = '0';
                input.max = String(l.cantidad);
                input.step = '1';
                input.inputMode = 'numeric';
                input.className = 'pf-input-cantidad';
                input.name = 'cantidades[' + l.id + '_' + (k + 1) + ']';
                input.value = reparto[i][k];
                input.setAttribute('aria-label', 'Unidades en la parte ' + (k + 1));
                input.dataset.i = i;
                input.dataset.k = k;
                td.appendChild(input);
                tr.insertBefore(td, ultima);
            }
        });
    }

    function actualizar() {
        var errores = [];

        lineas.forEach(function (l, i) {
            var repartidas = reparto[i].reduce(function (s, u) { return s + u; }, 0);
            var diferencia = l.cantidad - repartidas;
            var celda = filas[l.id].querySelector('[data-por-repartir]');
            celda.textContent = diferencia === 0 ? '✓' : (diferencia > 0 ? 'Faltan ' + diferencia : 'Sobran ' + (-diferencia));
            celda.classList.toggle('is-ok', diferencia === 0);
            celda.classList.toggle('is-error', diferencia !== 0);
            if (diferencia !== 0) errores.push(diferencia > 0 ? 'faltan unidades por repartir' : 'hay unidades de más');
        });

        resumen.innerHTML = '';
        var algunaExcede = false;
        for (var k = 0; k < partes; k++) {
            var unidades = unidadesParte(k);
            var minutos = tiempoParte(k);
            var excede = minutos > cfg.jornada;
            if (excede) algunaExcede = true;
            var porcentaje = Math.min(100, minutos / cfg.jornada * 100);
            if (unidades === 0) errores.push('la parte ' + (k + 1) + ' está vacía');

            var tarjeta = document.createElement('div');
            tarjeta.className = 'pf-parte-card' + (excede ? ' is-excede' : '') + (unidades === 0 ? ' is-vacia' : '');
            tarjeta.innerHTML =
                '<div class="pf-parte-card__titulo">Parte ' + (k + 1) + '</div>' +
                '<div class="pf-parte-card__tiempo">' + (unidades === 0 ? 'Vacía' : duracion(minutos)) + '</div>' +
                '<div class="pf-parte-card__barra" role="img" aria-label="' + Math.round(minutos / cfg.jornada * 100) + ' % de una jornada"><span style="width:' + porcentaje + '%"></span></div>' +
                '<div class="pf-parte-card__detalle">' + unidades + ' uds' + (excede ? ' · supera una jornada' : '') + '</div>';
            resumen.appendChild(tarjeta);
        }

        var unicos = errores.filter(function (e, i) { return errores.indexOf(e) === i; });
        var valido = unicos.length === 0;
        btnDividir.disabled = !valido;
        btnDividir.textContent = 'Dividir en ' + partes + ' partes';
        mensaje.className = 'pf-mensaje-reparto mt-3 ' + (valido ? 'is-ok' : 'is-error');
        mensaje.textContent = valido
            ? (algunaExcede
                ? 'Todo repartido. Hay partes que superan una jornada a ritmo estándar; considere agregar partes.'
                : 'Todo repartido: cada parte cabe en una jornada a ritmo estándar.')
            : 'Para continuar: ' + unicos.join(', ') + '.';
    }

    function cambiarPartes(n) {
        n = Math.max(2, Math.min(cfg.maxPartes, parseInt(n, 10) || 2));
        partes = n;
        campoPartes.value = n;
        document.getElementById('btnMenosPartes').disabled = n <= 2;
        document.getElementById('btnMasPartes').disabled = n >= cfg.maxPartes;
        repartir(n);
        construirColumnas(n);
        actualizar();
    }

    document.getElementById('btnMenosPartes').addEventListener('click', function () { cambiarPartes(partes - 1); });
    document.getElementById('btnMasPartes').addEventListener('click', function () { cambiarPartes(partes + 1); });
    campoPartes.addEventListener('change', function () { cambiarPartes(campoPartes.value); });
    document.getElementById('btnRepartir').addEventListener('click', function () { cambiarPartes(partes); });

    document.getElementById('tablaReparto').addEventListener('input', function (e) {
        var input = e.target;
        if (!input.classList.contains('pf-input-cantidad')) return;
        var valor = parseInt(input.value, 10);
        reparto[+input.dataset.i][+input.dataset.k] = isNaN(valor) || valor < 0 ? 0 : valor;
        actualizar();
    });

    cambiarPartes(cfg.partesSugeridas);
})();
