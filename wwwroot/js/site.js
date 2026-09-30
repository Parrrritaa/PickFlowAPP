// JavaScript compartido por todas las páginas (se carga en _Layout.cshtml y _LayoutLogin.cshtml).
// Cada bloque escucha eventos en el documento y actúa solo sobre los elementos marcados con sus
// atributos data-*, así funciona en cualquier vista sin repetir código.

// "Ver más / Ver menos" para tablas largas: el botón [data-ver-mas="idTabla"] muestra u oculta
// las filas .fila-extra de esa tabla. Guarda el estado en data-expandido.
document.addEventListener('click', function (e) {
    var btn = e.target.closest('[data-ver-mas]');
    if (!btn) return;
    var tabla = document.getElementById(btn.dataset.verMas);
    if (!tabla) return;
    var expandir = btn.dataset.expandido !== 'true';
    tabla.querySelectorAll('.fila-extra').forEach(function (f) {
        f.classList.toggle('d-none', !expandir);
        // Reinicia la animación de aparición en cada despliegue
        f.classList.remove('pf-reveal');
        if (expandir) {
            void f.offsetWidth;
            f.classList.add('pf-reveal');
        }
    });
    btn.dataset.expandido = expandir ? 'true' : 'false';
    btn.textContent = expandir ? btn.dataset.textoMenos : btn.dataset.textoMas;
});

// Desplegar/ocultar las partes de un pedido dividido (Pedidos > Divididos): el botón
// [data-target] alterna .is-expanded en la fila del pedido; el CSS anima la altura
// de la fila de partes correspondiente vía el selector de hermanos.
document.addEventListener('click', function (e) {
    var btn = e.target.closest('.pf-btn-toggle-partes');
    if (!btn) return;
    var fila = btn.closest('tr');
    if (!fila) return;
    var expandido = !fila.classList.contains('is-expanded');
    fila.classList.toggle('is-expanded', expandido);
    btn.setAttribute('aria-expanded', expandido ? 'true' : 'false');
});

// Pestañas de vista (Pedidos / Divididos): al elegir otra pestaña, la píldora blanca se desliza
// hasta ella y al terminar se navega. Sin JavaScript siguen siendo enlaces con su propio fondo.
(function () {
    document.querySelectorAll('.pf-vista-tabs').forEach(function (nav) {
        var tabs = Array.prototype.slice.call(nav.querySelectorAll('a'));
        var inicial = nav.querySelector('a.activo');
        if (!inicial) return;

        var indicador = document.createElement('span');
        indicador.className = 'pf-vista-tabs__indicador';
        indicador.setAttribute('aria-hidden', 'true');
        nav.insertBefore(indicador, nav.firstChild);
        nav.classList.add('pf-vista-tabs--animada');

        var navegando = false;

        function colocar(tab, animar) {
            indicador.classList.toggle('sin-transicion', !animar);
            indicador.style.width = tab.offsetWidth + 'px';
            indicador.style.transform = 'translateX(' + tab.offsetLeft + 'px)';
        }

        function marcar(tab) {
            tabs.forEach(function (t) { t.classList.toggle('activo', t === tab); });
        }

        colocar(inicial, false);

        // Si cambia el ancho de las pestañas (carga de la fuente, zoom) la píldora se reajusta sin animar
        if (window.ResizeObserver) {
            new ResizeObserver(function () {
                if (!navegando) colocar(inicial, false);
            }).observe(nav);
        }

        nav.addEventListener('click', function (e) {
            var tab = e.target.closest('a');
            if (!tab) return;
            if (navegando) {
                e.preventDefault();
                return;
            }
            // Clic en la pestaña actual, con modificadores (abrir en otra pestaña) o sin animaciones: comportamiento normal
            if (tab === inicial || e.button !== 0 || e.ctrlKey || e.metaKey || e.shiftKey || e.altKey || pfReducirMovimiento()) return;

            e.preventDefault();
            navegando = true;
            marcar(tab);
            colocar(tab, true);

            // Se llama al acabar la transición o por el temporizador de respaldo: solo navega la primera vez,
            // y nunca si la página se restauró con "atrás" mientras tanto
            var yaNavego = false;
            var ir = function () {
                if (!navegando || yaNavego) return;
                yaNavego = true;
                window.location.href = tab.href;
            };
            indicador.addEventListener('transitionend', ir, { once: true });
            window.setTimeout(ir, 400); // por si la transición no llega a dispararse
        });

        // Al volver con "atrás" el navegador puede restaurar la página con la píldora en la otra pestaña
        window.addEventListener('pageshow', function (e) {
            if (!e.persisted) return;
            navegando = false;
            marcar(inicial);
            colocar(inicial, false);
        });
    });
})();

// Ojito para mostrar/ocultar contraseña: envuelve cada input[type="password"] con un botón de alternar,
// sin necesidad de tocarlo en cada vista. Corre una sola vez por input (marca data-pf-ojito).
document.addEventListener('DOMContentLoaded', function () {
    document.querySelectorAll('input[type="password"]').forEach(function (input) {
        if (input.dataset.pfOjito) return;
        input.dataset.pfOjito = 'true';

        var wrapper = document.createElement('div');
        wrapper.className = 'pf-password-field';
        input.parentNode.insertBefore(wrapper, input);
        wrapper.appendChild(input);
        input.classList.add('pf-password-field__input');
        // El margen inferior del input pasa al contenedor: si se queda en el input, el contenedor
        // queda más alto que el campo y el ojito (centrado con top: 50%) se ve desplazado hacia abajo
        wrapper.style.marginBottom = getComputedStyle(input).marginBottom;
        input.style.marginBottom = '0';

        var btn = document.createElement('button');
        btn.type = 'button';
        btn.className = 'pf-password-toggle';
        btn.setAttribute('aria-label', 'Mostrar contraseña');
        btn.innerHTML = '<i class="bi bi-eye" aria-hidden="true"></i>';
        wrapper.appendChild(btn);

        btn.addEventListener('click', function () {
            var mostrar = input.type === 'password';
            input.type = mostrar ? 'text' : 'password';
            btn.setAttribute('aria-label', mostrar ? 'Ocultar contraseña' : 'Mostrar contraseña');

            var iconoViejo = btn.querySelector('i');
            var icono = document.createElement('i');
            icono.className = 'bi ' + (mostrar ? 'bi-eye-slash' : 'bi-eye');
            icono.setAttribute('aria-hidden', 'true');
            btn.replaceChild(icono, iconoViejo);

            input.focus({ preventScroll: true });
        });
    });
});

// Modales de confirmación sobre un registro de una tabla: el botón que abre el modal lleva
// data-pf-id y data-pf-texto, y el modal marca dónde van con [data-pf-recibe="id"] (value)
// y [data-pf-recibe="texto"] (textContent). Los datos se copian como texto, nunca como código,
// para que un nombre con comillas o HTML no rompa el botón ni se ejecute.
document.addEventListener('show.bs.modal', function (e) {
    var btn = e.relatedTarget;
    if (!btn || !btn.hasAttribute('data-pf-id')) return;
    e.target.querySelectorAll('[data-pf-recibe="id"]').forEach(function (el) {
        el.value = btn.dataset.pfId;
    });
    e.target.querySelectorAll('[data-pf-recibe="texto"]').forEach(function (el) {
        el.textContent = btn.dataset.pfTexto || '';
    });
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

function pfReducirMovimiento() {
    return window.matchMedia('(prefers-reduced-motion: reduce)').matches;
}

// Confirmación de cierre de sesión con un diálogo propio (reemplaza el confirm() del navegador).
// Sin JavaScript el formulario sigue funcionando y cierra la sesión directamente.
(function () {
    var form = document.getElementById('pf-logout-form');
    var modalEl = document.getElementById('pfModalCerrarSesion');
    var btnConfirmar = document.getElementById('pfConfirmarCerrarSesion');
    if (!form || !modalEl || !btnConfirmar || !window.bootstrap) return;

    var modal = bootstrap.Modal.getOrCreateInstance(modalEl);
    var confirmado = false;

    form.addEventListener('submit', function (e) {
        if (confirmado) return;
        e.preventDefault();
        modal.show();
    });

    // Cerrar sesión fue una elección deliberada: el botón de la acción queda como predeterminado (Enter)
    modalEl.addEventListener('shown.bs.modal', function () { btnConfirmar.focus(); });

    // Al cancelar, el foco vuelve al botón que abrió el diálogo (Bootstrap no lo hace al abrirlo por código)
    modalEl.addEventListener('hidden.bs.modal', function () {
        if (!confirmado) {
            var btnAbrir = form.querySelector('[type="submit"]');
            if (btnAbrir) btnAbrir.focus({ preventScroll: true });
        }
    });

    btnConfirmar.addEventListener('click', function () {
        if (confirmado) return;
        confirmado = true;
        btnConfirmar.classList.add('is-loading');
        btnConfirmar.insertAdjacentHTML('afterbegin', '<span class="pf-spinner" aria-hidden="true"></span>');

        modalEl.addEventListener('hidden.bs.modal', function () {
            if (pfReducirMovimiento()) {
                form.submit();
                return;
            }
            // Fundido de salida del panel antes de ir al login
            document.body.classList.add('pf-logging-out');
            window.setTimeout(function () { form.submit(); }, 220);
        }, { once: true });

        modal.hide();
    });
})();

// Estado "procesando" en los formularios POST: indicador en el botón y bloqueo del doble envío.
// Los formularios que manejan su propio envío (preventDefault) o llevan data-sin-carga se omiten.
document.addEventListener('submit', function (e) {
    var form = e.target;
    if (e.defaultPrevented || !(form instanceof HTMLFormElement)) return;
    if ((form.getAttribute('method') || '').toLowerCase() !== 'post' || form.hasAttribute('data-sin-carga')) return;

    if (form.dataset.enviando === 'true') {
        e.preventDefault();
        return;
    }
    form.dataset.enviando = 'true';
    form.setAttribute('aria-busy', 'true');

    // No se usa "disabled": un botón deshabilitado no envía su name/value
    var btn = e.submitter || form.querySelector('[type="submit"]');
    if (btn && !btn.querySelector('.pf-spinner')) {
        btn.classList.add('is-loading');
        btn.setAttribute('aria-disabled', 'true');
        btn.insertAdjacentHTML('afterbegin', '<span class="pf-spinner" aria-hidden="true"></span>');
    }
});

// Al volver con el botón "atrás" el navegador puede restaurar la página tal como quedó: se limpia el estado
window.addEventListener('pageshow', function (e) {
    if (!e.persisted) return;
    document.querySelectorAll('form[data-enviando]').forEach(function (form) {
        delete form.dataset.enviando;
        form.removeAttribute('aria-busy');
    });
    document.querySelectorAll('.is-loading').forEach(function (btn) {
        btn.classList.remove('is-loading');
        btn.removeAttribute('aria-disabled');
        var spinner = btn.querySelector('.pf-spinner');
        if (spinner) spinner.remove();
    });
    document.body.classList.remove('pf-logging-out');
});

// Notificaciones: marcar como leída sin recargar y descartarla con una animación.
// Sin JavaScript, el formulario hace el POST normal y la página se recarga.
(function () {
    var lista = document.getElementById('pfNotifLista');
    if (!lista) return;
    var contador = document.getElementById('pfNotifContador');
    var vacio = document.getElementById('pfNotifVacio');

    function textoContador(n) {
        return n === 1 ? '1 notificación sin leer' : n + ' notificaciones sin leer';
    }

    function actualizarContador() {
        var restantes = lista.querySelectorAll('.pf-notif').length;
        if (contador) contador.textContent = restantes > 0 ? textoContador(restantes) : '';
        if (restantes === 0 && vacio) {
            vacio.hidden = false;
            vacio.classList.add('pf-appear');
            vacio.focus({ preventScroll: true });
        }
    }

    // Sale hacia la derecha con un fundido y luego cierra su espacio para que el resto suba suavemente
    function descartar(item) {
        if (pfReducirMovimiento() || !item.animate) {
            item.remove();
            return Promise.resolve();
        }
        var estilo = getComputedStyle(item);
        var desde = {
            height: item.offsetHeight + 'px',
            marginBottom: estilo.marginBottom,
            paddingTop: estilo.paddingTop,
            paddingBottom: estilo.paddingBottom
        };
        var salida = item.animate(
            [{ opacity: 1, transform: 'none' }, { opacity: 0, transform: 'translateX(28px)' }],
            { duration: 240, easing: 'cubic-bezier(0.4, 0, 1, 1)', fill: 'forwards' });

        return salida.finished.then(function () {
            item.style.overflow = 'hidden';
            return item.animate(
                [desde, { height: '0px', marginBottom: '0px', paddingTop: '0px', paddingBottom: '0px' }],
                { duration: 220, easing: 'cubic-bezier(0.4, 0, 0.2, 1)', fill: 'forwards' }).finished;
        }).then(function () { item.remove(); });
    }

    lista.addEventListener('submit', function (e) {
        var form = e.target.closest('form[data-marcar-leida]');
        if (!form) return;
        e.preventDefault();

        var item = form.closest('.pf-notif');
        var btn = form.querySelector('.pf-notif-read');
        if (item.dataset.estado === 'enviando') return;
        item.dataset.estado = 'enviando';

        var contenidoOriginal = btn.innerHTML;
        var error = item.querySelector('.pf-notif-error');
        if (error) error.remove();

        // Confirmación inmediata en el botón mientras responde el servidor
        btn.classList.add('is-done');
        btn.setAttribute('aria-disabled', 'true');
        btn.innerHTML = '<i class="bi bi-check2" aria-hidden="true"></i><span>Leída</span>';

        var pausa = new Promise(function (resolve) { setTimeout(resolve, pfReducirMovimiento() ? 0 : 220); });
        var peticion = fetch(form.action, {
            method: 'POST',
            body: new FormData(form),
            headers: { 'X-Requested-With': 'XMLHttpRequest' },
            credentials: 'same-origin'
        }).then(function (r) {
            if (!r.ok) throw new Error('HTTP ' + r.status);
        });

        Promise.all([peticion, pausa]).then(function () {
            // Conserva el foco del teclado en la siguiente notificación (o la anterior)
            var vecino = item.nextElementSibling || item.previousElementSibling;
            var teniaFoco = item.contains(document.activeElement);
            return descartar(item).then(function () {
                if (teniaFoco && vecino) {
                    var siguienteBtn = vecino.querySelector('.pf-notif-read');
                    if (siguienteBtn) siguienteBtn.focus({ preventScroll: true });
                }
                actualizarContador();
            });
        }).catch(function () {
            item.dataset.estado = '';
            btn.classList.remove('is-done');
            btn.removeAttribute('aria-disabled');
            btn.innerHTML = contenidoOriginal;
            item.querySelector('.pf-notif-body').insertAdjacentHTML('beforeend',
                '<div class="pf-notif-error" role="alert">No se pudo marcar como leída. Inténtalo de nuevo.</div>');
        });
    });
})();

// Crear/Editar ubicación: elegir un producto existente o registrar uno nuevo (Views/Bodegas/_SeleccionProducto.cshtml).
// Solo se envía el bloque visible; el otro queda deshabilitado. Un código de producto repetido se avisa
// mientras se escribe, y el servidor lo vuelve a validar al guardar.
(function () {
    var bloque = document.querySelector('[data-seleccion-producto]');
    if (!bloque) return;

    var datos = document.getElementById('datosProductosUbicacion');
    var productos = datos ? JSON.parse(datos.textContent) : [];
    var lista = bloque.querySelector('[data-lista-productos]');
    var buscar = bloque.querySelector('[data-buscar-producto]');
    var sinCoincidencias = bloque.querySelector('[data-sin-coincidencias]');
    var resumen = bloque.querySelector('[data-resumen-producto]');
    var numero = bloque.querySelector('[data-numero-producto]');
    var codigoTexto = bloque.querySelector('[data-codigo-producto]');
    var codigoTextoInicial = codigoTexto.textContent;
    var aviso = bloque.querySelector('[data-aviso-repetido]');
    var avisoTexto = bloque.querySelector('[data-aviso-texto]');
    var repetido = null;

    // Entre 3 y 6 filas visibles: sin espacio vacío con pocos productos ni una lista enorme con muchos
    function filasLista(cantidad) {
        return Math.min(6, Math.max(3, cantidad));
    }

    function productoPorId(id) {
        return productos.filter(function (p) { return String(p.id) === String(id); })[0] || null;
    }

    function elegirModo(modo, animar) {
        var radio = bloque.querySelector('input[name="seleccion.Modo"][value="' + modo + '"]');
        if (radio) radio.checked = true;
        bloque.querySelectorAll('[data-si-modo]').forEach(function (b) {
            var visible = b.dataset.siModo === modo;
            b.hidden = !visible;
            b.querySelectorAll('input, select').forEach(function (c) { c.disabled = !visible; });
            if (visible && animar) {
                b.classList.remove('pf-appear');
                void b.offsetWidth;
                b.classList.add('pf-appear');
            }
        });
    }

    // Categoría, cantidad por caja y ubicaciones del producto elegido; se escribe como texto, nunca como HTML
    function actualizarResumen() {
        var p = productoPorId(lista.value);
        resumen.hidden = !p;
        resumen.textContent = '';
        if (!p) return;
        [
            ['bi-tag', p.categoria],
            ['bi-box-seam', p.unidadesPorCaja === 1 ? '1 unidad por caja' : p.unidadesPorCaja + ' unidades por caja'],
            ['bi-geo-alt', p.ubicaciones === 0 ? 'Aún sin ubicaciones' : (p.ubicaciones === 1 ? 'En 1 ubicación' : 'En ' + p.ubicaciones + ' ubicaciones')]
        ].forEach(function (dato) {
            var span = document.createElement('span');
            var icono = document.createElement('i');
            icono.className = 'bi ' + dato[0];
            icono.setAttribute('aria-hidden', 'true');
            span.appendChild(icono);
            span.appendChild(document.createTextNode(dato[1]));
            resumen.appendChild(span);
        });
    }

    // La lista se reconstruye con los productos que coinciden, conservando el elegido si sigue en ella
    function filtrar() {
        var texto = buscar.value.trim().toLowerCase();
        var elegido = lista.value;
        var coincidencias = productos.filter(function (p) {
            return (p.codigo + ' ' + p.nombre).toLowerCase().indexOf(texto) !== -1;
        });
        lista.textContent = '';
        coincidencias.forEach(function (p) {
            var opcion = new Option(p.codigo + ' · ' + p.nombre, p.id);
            opcion.selected = String(p.id) === elegido;
            lista.add(opcion);
        });
        sinCoincidencias.hidden = coincidencias.length > 0;
        lista.size = filasLista(coincidencias.length);
        actualizarResumen();
    }

    // Mismo código que genera el servidor: PROD- y el número con al menos 3 dígitos
    function revisarCodigo() {
        var n = numero.value.trim();
        repetido = null;
        if (/^\d+$/.test(n)) {
            var valor = String(parseInt(n, 10));
            var codigo = 'PROD-' + (valor.length < 3 ? ('000' + valor).slice(-3) : valor);
            codigoTexto.textContent = 'Código: ' + codigo;
            repetido = productos.filter(function (p) { return p.codigo.toUpperCase() === codigo; })[0] || null;
        } else {
            codigoTexto.textContent = codigoTextoInicial;
        }
        aviso.hidden = !repetido;
        numero.setCustomValidity(repetido ? 'Ese producto ya existe: elige «Producto existente».' : '');
        if (repetido) avisoTexto.textContent = repetido.codigo + ' ya existe (' + repetido.nombre + ').';
    }

    bloque.addEventListener('change', function (e) {
        if (e.target.name === 'seleccion.Modo') {
            elegirModo(e.target.value, true);
            if (e.target.value === 'nuevo') revisarCodigo();
        } else if (e.target === lista) {
            actualizarResumen();
        }
    });

    buscar.hidden = false;
    buscar.addEventListener('input', filtrar);
    buscar.addEventListener('keydown', function (e) {
        // Enter no envía el formulario desde el buscador: elige la única coincidencia, si la hay
        if (e.key === 'Enter') {
            e.preventDefault();
            if (lista.options.length === 1) {
                lista.selectedIndex = 0;
                actualizarResumen();
            }
        } else if (e.key === 'ArrowDown' && lista.options.length > 0) {
            e.preventDefault();
            if (lista.selectedIndex < 0) lista.selectedIndex = 0;
            lista.focus();
            actualizarResumen();
        }
    });

    numero.addEventListener('input', revisarCodigo);

    // "Usar ese producto": pasa a Producto existente con el producto repetido ya elegido
    bloque.querySelector('[data-usar-existente]').addEventListener('click', function () {
        if (!repetido) return;
        var id = repetido.id;
        elegirModo('existente', true);
        buscar.value = '';
        filtrar();
        lista.value = String(id);
        actualizarResumen();
        lista.focus();
    });

    // Sin coincidencias en la búsqueda: pasa a Producto nuevo con lo escrito como número o como nombre
    bloque.querySelector('[data-ir-a-nuevo]').addEventListener('click', function () {
        var texto = buscar.value.trim();
        var nombre = bloque.querySelector('#nombreProducto');
        elegirModo('nuevo', true);
        if (/^\d+$/.test(texto)) {
            numero.value = texto;
        } else if (texto && !nombre.value) {
            nombre.value = texto;
        }
        revisarCodigo();
        (numero.value ? nombre : numero).focus();
    });

    actualizarResumen();
    revisarCodigo();
})();
