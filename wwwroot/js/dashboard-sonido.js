// Easter egg del Dashboard: al tocar el título suena una trompeta, como la nariz de Freddy en el póster
// de Five Nights at Freddy's. La ruta del audio llega en data-sonido (Views/Home/Index.cshtml); el archivo
// pesa unos 28 KB, así que se descarga con la página para que suene al instante desde el primer toque.
(function () {
    var titulo = document.getElementById('pfDashTitulo');
    if (!titulo || !titulo.dataset.sonido) return;

    var sonido = new Audio(titulo.dataset.sonido);
    sonido.preload = 'auto';

    titulo.addEventListener('click', function () {
        // Cada toque lo empieza de nuevo, aunque el anterior no haya terminado
        sonido.currentTime = 0;
        // play() devuelve una promesa que falla si el navegador bloquea el audio: el easter egg no muestra errores
        var reproduccion = sonido.play();
        reproduccion?.catch(function () { });

        // Apretón visual; se reinicia si se toca varias veces seguidas
        titulo.classList.remove('pf-boop');
        void titulo.offsetWidth;
        titulo.classList.add('pf-boop');
    });
    titulo.addEventListener('animationend', function (e) {
        if (e.animationName === 'pf-boop') titulo.classList.remove('pf-boop');
    });
})();
