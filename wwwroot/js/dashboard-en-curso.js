// Inicio (Views/Home/Index.cshtml): avance de los pedidos en curso de "Pedidos recientes".
// Cada pedido trae la hora en que empezó y su tiempo estimado (data-inicio, data-estimado); la barra avanza
// a ritmo constante según el reloj y el texto dice cuánto falta o cuánto lleva de retraso. Es una estimación
// por tiempo, no el progreso real: el pedido solo cambia de estado al completarlo.
(function () {
    var filas = document.querySelectorAll('.pf-reciente[data-inicio][data-estimado]');
    if (!filas.length) return;

    function minutos(n) {
        return n === 1 ? '1 min' : n + ' min';
    }

    function actualizar() {
        var ahora = Date.now();
        filas.forEach(function (fila) {
            var inicio = new Date(fila.dataset.inicio).getTime();
            var estimado = parseFloat(fila.dataset.estimado);
            if (isNaN(inicio) || !(estimado > 0)) return;
            var transcurrido = Math.max(0, (ahora - inicio) / 60000);
            var avance = Math.min(1, transcurrido / estimado);
            var tarde = transcurrido > estimado;

            fila.querySelector('.pf-reciente-barra span').style.transform = 'scaleX(' + avance.toFixed(4) + ')';
            fila.classList.toggle('is-tarde', tarde);
            var texto = fila.querySelector('[data-estado-texto]');
            texto.textContent = tarde
                ? minutos(Math.ceil(transcurrido - estimado)) + ' tarde'
                : 'Faltan ' + minutos(Math.max(1, Math.ceil(estimado - transcurrido)));
        });
    }

    actualizar();
    window.setInterval(actualizar, 1000);
})();
