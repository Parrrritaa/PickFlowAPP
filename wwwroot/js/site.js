// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Write your JavaScript code.

// "Ver más / Ver menos" para tablas largas: el botón [data-ver-mas="idTabla"] muestra u oculta
// las filas .fila-extra de esa tabla. Guarda el estado en data-expandido.
document.addEventListener('click', function (e) {
    var btn = e.target.closest('[data-ver-mas]');
    if (!btn) return;
    var tabla = document.getElementById(btn.dataset.verMas);
    if (!tabla) return;
    var expandir = btn.dataset.expandido !== 'true';
    tabla.querySelectorAll('.fila-extra').forEach(function (f) { f.classList.toggle('d-none', !expandir); });
    btn.dataset.expandido = expandir ? 'true' : 'false';
    btn.textContent = expandir ? btn.dataset.textoMenos : btn.dataset.textoMas;
});

// Menú lateral desplegable.
// Escritorio: se oculta/muestra empujando el contenido y el estado se recuerda en este navegador.
// Pantallas angostas: se superpone al contenido, arranca oculto y se cierra con Escape o tocando fuera.
(function () {
    var root = document.documentElement;

    // Quita el bloqueo de animación de la carga inicial (lo pone el script del <head> del layout)
    requestAnimationFrame(function () {
        requestAnimationFrame(function () { root.classList.remove('pf-sin-transicion'); });
    });

    var sidebar = document.getElementById('pfSidebar');
    var backdrop = document.getElementById('pfSidebarBackdrop');
    var botones = document.querySelectorAll('[data-menu-toggle]');
    if (!sidebar || !botones.length) return;
    var btnMenu = sidebar.querySelector('[data-menu-toggle]');
    var btnHeader = document.querySelector('.pf-menu-toggle--header');

    var pantallaAngosta = window.matchMedia('(max-width: 991.98px)');

    function menuVisible() {
        return pantallaAngosta.matches
            ? root.classList.contains('pf-sidebar-open')
            : !root.classList.contains('pf-sidebar-collapsed');
    }

    function sincronizarBoton() {
        var visible = menuVisible();
        var texto = visible ? 'Ocultar menú' : 'Mostrar menú';
        botones.forEach(function (b) {
            b.setAttribute('aria-expanded', visible ? 'true' : 'false');
            b.setAttribute('aria-label', texto);
            b.title = texto;
        });
    }

    function mostrarMenu(visible) {
        if (pantallaAngosta.matches) {
            root.classList.toggle('pf-sidebar-open', visible);
        } else {
            root.classList.toggle('pf-sidebar-collapsed', !visible);
            try { localStorage.setItem('pf-menu', visible ? 'visible' : 'oculto'); } catch (e) { }
        }
        sincronizarBoton();

        // El foco pasa al botón que queda visible, para no perderlo al esconderse el que se pulsó
        var destino = visible ? btnMenu : btnHeader;
        if (destino) destino.focus({ preventScroll: true });
    }

    botones.forEach(function (b) {
        b.addEventListener('click', function () { mostrarMenu(!menuVisible()); });
    });

    if (backdrop) {
        backdrop.addEventListener('click', function () { mostrarMenu(false); });
    }

    document.addEventListener('keydown', function (e) {
        if (e.key === 'Escape' && pantallaAngosta.matches && menuVisible()) {
            mostrarMenu(false);
        }
    });

    // Al cruzar el punto de corte se cierra el panel superpuesto y el botón refleja el modo actual
    pantallaAngosta.addEventListener('change', function () {
        root.classList.remove('pf-sidebar-open');
        sincronizarBoton();
    });

    sincronizarBoton();
})();
