// JavaScript compartido por todas las páginas (se carga en _Layout.cshtml y _LayoutLogin.cshtml).
// Cada bloque escucha eventos en el documento y actúa solo sobre los elementos marcados con sus
// atributos data-*, así funciona en cualquier vista sin repetir código.

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

// Filas que se abren con un clic en cualquier parte: siguen al enlace [data-fila-abre] de esa fila
// (no al de una fila anidada). Los enlaces, botones y campos de la fila conservan su propio clic.
document.addEventListener('click', function (e) {
    if (e.target.closest('a, button, input, select, textarea, label')) return;
    var fila = e.target.closest('tr, li');
    if (!fila) return;
    var enlace = Array.prototype.find.call(fila.querySelectorAll('[data-fila-abre]'), function (a) {
        return a.closest('tr, li') === fila;
    });
    if (enlace) enlace.click();
});

// Validación en el navegador antes de enviar: un error que solo detectara el servidor recargaría la página, y el
// navegador nunca vuelve a llenar los campos de contraseña. Lo simple va en atributos HTML que el navegador ya
// respeta (required, pattern, minlength, type="email"); aquí solo lo que esos atributos no cubren:
//  - [data-pf-igual="nombre"]: debe coincidir con el campo de ese name (confirmar contraseña).
//  - [data-pf-correo-disponible="url"]: pregunta al servidor (POST, true/false) si el correo ya está en uso.
//  - [data-pf-oculto-si="idSelect=valor"]: oculta el bloque y desactiva sus campos (no se validan ni se envían)
//    mientras ese select tenga ese valor; p. ej. la bodega, que no aplica al rol Administrador.
(function () {
    document.querySelectorAll('[data-pf-oculto-si]').forEach(function (bloque) {
        var partes = bloque.dataset.pfOcultoSi.split('=');
        var select = document.getElementById(partes[0]);
        if (!select) return;
        function aplicar() {
            var oculto = select.value === partes[1];
            bloque.hidden = oculto;
            bloque.querySelectorAll('input, select').forEach(function (c) { c.disabled = oculto; });
        }
        select.addEventListener('change', aplicar);
        aplicar();
    });

    document.querySelectorAll('[data-pf-igual]').forEach(function (campo) {
        var otro = campo.form.elements[campo.dataset.pfIgual];
        function comparar() {
            campo.setCustomValidity(campo.value && campo.value !== otro.value ? 'Las contraseñas no coinciden.' : '');
        }
        campo.addEventListener('input', comparar);
        otro.addEventListener('input', comparar);
    });

    document.querySelectorAll('[data-pf-correo-disponible]').forEach(function (campo) {
        var form = campo.form;
        var revisado = null; // correo ya consultado (con su resultado aplicado)

        function consultar() {
            var correo = campo.value.trim().toLowerCase();
            campo.setCustomValidity('');
            if (!correo || !campo.checkValidity()) return Promise.resolve();
            var datos = new URLSearchParams({ correo: correo, __RequestVerificationToken: form.elements['__RequestVerificationToken'].value });
            return fetch(campo.dataset.pfCorreoDisponible, { method: 'POST', body: datos, credentials: 'same-origin' })
                .then(function (r) { return r.ok ? r.json() : true; })
                .then(function (libre) {
                    if (campo.value.trim().toLowerCase() !== correo) return; // lo cambiaron mientras respondía
                    revisado = correo;
                    campo.setCustomValidity(libre ? '' : 'Ese correo ya está en uso por otro usuario o por un empleado.');
                    if (!libre) campo.reportValidity();
                })
                .catch(function () { revisado = correo; }); // sin respuesta: lo decide el servidor al enviar
        }

        campo.addEventListener('input', function () { campo.setCustomValidity(''); revisado = null; });
        campo.addEventListener('change', consultar);
        // Si se envía antes de que llegue la respuesta, se espera y luego se reenvía (o se muestra el aviso)
        form.addEventListener('submit', function (e) {
            if (revisado === campo.value.trim().toLowerCase()) return;
            e.preventDefault();
            consultar().then(function () {
                if (campo.checkValidity()) form.requestSubmit(); else campo.reportValidity();
            });
        });
    });
})();

// Paginación según la pantalla: el usuario ve en cada página las filas que caben en la ventana (ver filasQueCaben),
// para desplazarse lo menos posible. Dos casos:
//  - [data-pf-paginar] (tabla o lista): la página trae todas las filas y aquí se reparten en páginas, con la
//    misma barra .pf-paginacion de _Paginacion.cshtml. Las filas que oculta un filtro de la vista (.d-none) no
//    cuentan; tras filtrar, la vista lanza 'pf:repaginar' sobre la tabla ({ detail: { inicio: true } } vuelve a la 1).
//  - [data-pf-filas-servidor="lista"] (pedidos, historial): pagina el servidor; aquí solo se calcula el tamaño y se
//    guarda en la cookie pf_filas_<lista>, que lee Paginacion.FilasPorPantalla. En la primera visita, si no coincide,
//    se recarga una vez; después la medida nueva se usa en la próxima visita.
(function () {
    var MIN = 4, MAX = 50, PREDETERMINADO = 8, MIN_PRIMER_PANTALLAZO = 2;
    var RESERVA = 96; // barra de paginación + margen inferior de la tarjeta

    function filas(contenedor) {
        var cuerpo = contenedor.tBodies ? contenedor.tBodies[0] : contenedor;
        return cuerpo ? Array.prototype.slice.call(cuerpo.children) : [];
    }

    // Cuántas filas del alto promedio de "items" caben entre el inicio del cuerpo y el borde inferior de la ventana.
    // Una tabla que empieza más abajo del primer pantallazo se ajusta a la ventana completa, al desplazarse hasta ella.
    // Posición en el documento sin transformaciones: getBoundingClientRect incluye la animación de entrada de la
    // página (la tarjeta empieza unos px más abajo) y al cargar daba una fila menos de las que caben
    function topEnDocumento(el) {
        for (var y = 0; el; el = el.offsetParent) y += el.offsetTop;
        return y;
    }

    function filasQueCaben(contenedor, items) {
        // data-pf-paginar="6": la vista fija cuántas filas van por página
        var fijas = parseInt(contenedor.dataset.pfPaginar, 10);
        if (fijas > 0) return fijas;
        var medibles = items.filter(function (f) { return f.offsetHeight > 4; });
        if (!medibles.length) return PREDETERMINADO;
        var alto = medibles.reduce(function (suma, f) { return suma + f.offsetHeight; }, 0) / medibles.length;
        var cuerpo = contenedor.tBodies ? contenedor.tBodies[0] : contenedor;
        var barra = document.querySelector('.pf-topbar');
        // Lo que va debajo de la tabla en la misma tarjeta (p. ej. la barra "Guardar cambios") también resta espacio
        var debajo = 0;
        for (var s = (contenedor.closest('.table-responsive') || contenedor).nextElementSibling; s; s = s.nextElementSibling) {
            if (!s.hidden && !s.classList.contains('pf-paginacion')) debajo += s.offsetHeight + parseFloat(getComputedStyle(s).marginTop);
        }
        var espacio = window.innerHeight - topEnDocumento(cuerpo) - RESERVA - debajo;
        // Si la tabla empieza en el primer pantallazo se muestran las filas que caben, aunque sean menos de MIN
        // (p. ej. con un aviso arriba): así la paginación queda a la vista. Solo una tabla que empieza más abajo
        // se ajusta a la ventana completa.
        var caben = Math.floor(espacio / alto);
        if (caben >= MIN_PRIMER_PANTALLAZO) return Math.min(MAX, caben);
        var encabezado = contenedor.tHead ? contenedor.tHead.offsetHeight : 0;
        espacio = window.innerHeight - (barra ? barra.offsetHeight : 0) - encabezado - RESERVA - debajo;
        return Math.min(MAX, Math.max(MIN, Math.floor(espacio / alto)));
    }

    // Entrada escalonada de las filas de la página nueva; animationend (abajo) quita la clase para poder repetirla
    function animarFilas(items) {
        items.forEach(function (f) { f.classList.remove('pf-pagina-in'); });
        void document.body.offsetWidth;
        items.forEach(function (f, i) {
            f.style.setProperty('--pf-retraso', Math.min(i, 8) * 20 + 'ms');
            f.classList.add('pf-pagina-in');
        });
    }

    document.addEventListener('animationend', function (e) {
        if (e.target.classList.contains('pf-pagina-in')) e.target.classList.remove('pf-pagina-in');
    });

    // Las listas que pagina el servidor (_Paginacion.cshtml) cambian de página sin recargar: se trae la página
    // nueva y se reemplazan solo las filas y la barra, así no se repite la entrada de todo el apartado ni salta
    // el scroll. Si algo falla (sesión vencida, error), se navega normalmente.
    var urlMostrada = location.href.split('#')[0];
    document.addEventListener('click', function (e) {
        var enlace = e.target.closest('nav.pf-paginacion a[href]');
        if (!enlace || e.button !== 0 || e.ctrlKey || e.metaKey || e.shiftKey || e.altKey) return;
        var nav = enlace.closest('nav');
        var tabla = nav.parentElement.querySelector('table.pf-table');
        if (!tabla || !tabla.tBodies[0]) return;
        e.preventDefault();
        var indice = Array.prototype.indexOf.call(document.querySelectorAll('nav.pf-paginacion'), nav);
        var etiqueta = enlace.getAttribute('aria-label');
        tabla.classList.add('pf-paginando');
        fetch(enlace.href, { credentials: 'same-origin' })
            .then(function (r) {
                if (!r.ok || r.redirected) throw new Error();
                return r.text();
            })
            .then(function (html) {
                var doc = new DOMParser().parseFromString(html, 'text/html');
                var navNueva = doc.querySelectorAll('nav.pf-paginacion')[indice];
                var tablaNueva = navNueva && navNueva.parentElement.querySelector('table.pf-table');
                if (!tablaNueva || !tablaNueva.tBodies[0]) throw new Error();
                tabla.tBodies[0].replaceWith(tablaNueva.tBodies[0]);
                nav.replaceWith(navNueva);
                history.pushState(null, '', enlace.href);
                urlMostrada = enlace.href.split('#')[0];
                animarFilas(filas(tabla));
                var foco = navNueva.querySelector('a[aria-label="' + etiqueta + '"]') || navNueva.querySelector('[aria-current]');
                if (foco) foco.focus({ preventScroll: true });
            })
            .catch(function () { location.href = enlace.href; })
            .finally(function () { tabla.classList.remove('pf-paginando'); });
    });

    // Atrás/adelante tras paginar así: la URL cambia pero las filas no, se recarga la página que corresponde
    window.addEventListener('popstate', function () {
        if (location.href.split('#')[0] !== urlMostrada) location.reload();
    });

    // La página a la vista queda en la URL (?pagina=, ?pagina2= para la segunda tabla de la página): al volver a esta
    // lista con "Volver", atrás o recargando, se abre en la misma página. Las que pagina el servidor ya la llevan.
    function parametroPagina(contenedor) {
        var i = paginados.indexOf(contenedor);
        return i > 0 ? 'pagina' + (i + 1) : 'pagina';
    }

    function recordarPagina(contenedor, pagina) {
        var url = new URL(location.href);
        if (pagina > 1) url.searchParams.set(parametroPagina(contenedor), pagina);
        else url.searchParams.delete(parametroPagina(contenedor));
        if (url.href === location.href) return;
        history.replaceState(history.state, '', url);
        urlMostrada = location.href.split('#')[0]; // es la misma página: atrás/adelante no debe recargarla
    }

    // paginaPedida: la que eligió el usuario (o la de una fila destacada); sin ella se conserva la que estaba a la vista
    function paginar(contenedor, paginaPedida) {
        var estado = contenedor._pfPaginacion;
        var items = filas(contenedor).filter(function (f) { return !f.classList.contains('d-none'); });
        // Se mide con todas las filas a la vista; el navegador no pinta entre esto y el reparto de abajo
        items.forEach(function (f) { f.hidden = false; });
        var porPagina = filasQueCaben(contenedor, items);
        var total = items.length;
        var paginas = Math.max(1, Math.ceil(total / porPagina));
        // Si cambió el tamaño de la ventana, se sigue viendo la primera fila que estaba a la vista.
        // Una página elegida no se recalcula: con otro tamaño de página la cuenta la devolvía a la anterior.
        if (paginaPedida) estado.pagina = paginaPedida;
        else if (estado.porPagina) estado.pagina = Math.floor((estado.pagina - 1) * estado.porPagina / porPagina) + 1;
        estado.pagina = Math.min(Math.max(1, estado.pagina), paginas);
        estado.porPagina = porPagina;
        recordarPagina(contenedor, estado.pagina);
        var desde = (estado.pagina - 1) * porPagina;
        items.forEach(function (f, i) { f.hidden = i < desde || i >= desde + porPagina; });
        dibujarBarra(contenedor, estado, paginas, total, desde, porPagina);
    }

    function dibujarBarra(contenedor, estado, paginas, total, desde, porPagina) {
        var nav = estado.nav;
        if (!nav) {
            nav = estado.nav = document.createElement('nav');
            nav.className = 'pf-paginacion';
            nav.setAttribute('aria-label', 'Páginas');
            (contenedor.closest('.table-responsive') || contenedor).after(nav);
            nav.addEventListener('click', function (e) {
                var btn = e.target.closest('button[data-pagina]');
                if (!btn) return;
                var etiqueta = btn.getAttribute('aria-label');
                paginar(contenedor, Number(btn.dataset.pagina));
                animarFilas(filas(contenedor).filter(function (f) { return !f.hidden && !f.classList.contains('d-none'); }));
                // El botón pulsado se redibuja: el foco vuelve al mismo control (o a la página actual)
                var foco = nav.querySelector('[aria-label="' + etiqueta + '"]') || nav.querySelector('[aria-current]');
                if (foco) foco.focus({ preventScroll: true });
            });
        }
        nav.hidden = paginas <= 1;
        if (nav.hidden) return;

        function boton(pagina, contenido, etiqueta, activo) {
            return activo
                ? '<li><button type="button" data-pagina="' + pagina + '" aria-label="' + etiqueta + '">' + contenido + '</button></li>'
                : '<li><span class="deshabilitado" aria-hidden="true">' + contenido + '</span></li>';
        }

        var html = '<span class="pf-paginacion__resumen">' + (desde + 1) + ' a ' + Math.min(desde + porPagina, total) + ' de ' + total + '</span><ul>';
        html += boton(estado.pagina - 1, '<i class="bi bi-chevron-left" aria-hidden="true"></i>', 'Página anterior', estado.pagina > 1);
        // Primera, última y dos a cada lado de la actual, como _Paginacion.cshtml
        var salto = false;
        for (var p = 1; p <= paginas; p++) {
            if (p === 1 || p === paginas || Math.abs(p - estado.pagina) <= 2) {
                html += p === estado.pagina
                    ? '<li><span class="actual" aria-current="page" aria-label="Página ' + p + '" tabindex="-1">' + p + '</span></li>'
                    : boton(p, p, 'Página ' + p, true);
                salto = false;
            } else if (!salto) {
                html += '<li><span class="salto" aria-hidden="true">…</span></li>';
                salto = true;
            }
        }
        html += boton(estado.pagina + 1, '<i class="bi bi-chevron-right" aria-hidden="true"></i>', 'Página siguiente', estado.pagina < paginas);
        nav.innerHTML = html + '</ul>';
    }

    function ajustarServidor(contenedor) {
        var lista = contenedor.dataset.pfFilasServidor;
        var porPagina = filasQueCaben(contenedor, filas(contenedor));
        var guardada = (document.cookie.match(new RegExp('(?:^|; )pf_filas_' + lista + '=(\\d+)')) || [])[1];
        if (String(porPagina) === guardada) return false;
        document.cookie = 'pf_filas_' + lista + '=' + porPagina + '; path=/; max-age=31536000; SameSite=Lax';
        if (Number(contenedor.dataset.pfTamano) === porPagina) return false;
        // Solo se recarga la primera vez (sin medida guardada). Después la medida nueva se aplica en la próxima
        // visita: el alto de las filas cambia con su contenido y recargar cada vez cargaba la página dos veces.
        if (guardada) return false;
        // Una sola recarga seguida por página: si la medida cambiara al recargar, no se entra en un ciclo
        try {
            if (sessionStorage.getItem('pfFilasRecarga') === location.href) return false;
            sessionStorage.setItem('pfFilasRecarga', location.href);
        } catch (e) { return false; }
        location.reload();
        return true;
    }

    var paginados = Array.prototype.slice.call(document.querySelectorAll('[data-pf-paginar]'));
    paginados.forEach(function (contenedor) {
        // Si la vista resalta una fila recién creada o editada, se abre en su página
        var destacada = filas(contenedor).findIndex(function (f) { return f.classList.contains('pf-row-flash'); });
        contenedor._pfPaginacion = { pagina: Number(new URLSearchParams(location.search).get(parametroPagina(contenedor))) || 1 };
        contenedor.addEventListener('pf:repaginar', function (e) {
            paginar(contenedor, e.detail && e.detail.inicio ? 1 : undefined);
        });
        paginar(contenedor);
        if (destacada >= contenedor._pfPaginacion.porPagina) {
            paginar(contenedor, Math.floor(destacada / contenedor._pfPaginacion.porPagina) + 1);
        }
    });

    var delServidor = Array.prototype.slice.call(document.querySelectorAll('[data-pf-filas-servidor]'));
    // Tras cargar fuentes e imágenes las filas ya tienen su alto definitivo
    // Las fuentes web pueden llegar después de 'load' y cambiar el alto de las filas
    if (document.fonts) document.fonts.ready.then(function () { paginados.forEach(function (c) { paginar(c); }); });
    window.addEventListener('load', function () {
        paginados.forEach(function (c) { paginar(c); });
        if (!delServidor.some(ajustarServidor)) {
            try { sessionStorage.removeItem('pfFilasRecarga'); } catch (e) { }
        }
    });

    // Al cambiar el tamaño de la ventana (o cerrar un aviso, que deja más espacio) se reparten de nuevo;
    // las del servidor lo aplican en la próxima visita
    var espera;
    // 'closed.bs.alert' se lanza con el aviso ya fuera del documento y no llega aquí: se usa 'close' y se espera la animación
    document.addEventListener('close.bs.alert', function () {
        setTimeout(function () { window.dispatchEvent(new Event('resize')); }, 350);
    });
    window.addEventListener('resize', function () {
        clearTimeout(espera);
        espera = setTimeout(function () {
            paginados.forEach(function (c) { paginar(c); });
            delServidor.forEach(function (c) {
                var porPagina = filasQueCaben(c, filas(c));
                document.cookie = 'pf_filas_' + c.dataset.pfFilasServidor + '=' + porPagina + '; path=/; max-age=31536000; SameSite=Lax';
            });
        }, 150);
    });
})();

// "Volver" lleva a la página exacta donde estaba el usuario, con su página de la lista y sus filtros (van en la URL).
// En sessionStorage (por pestaña) cada página guarda de dónde se llegó a ella y la última URL de cada página visitada:
//  - [data-pf-volver]: vuelve a la página de origen (p. ej. del pedido a la lista, al Inicio o a las notificaciones).
//  - [data-pf-volver="ultima"]: el destino es su href (p. ej. "Volver al pedido"), en la última URL que tuvo.
// Sin datos guardados (pestaña nueva, enlace directo) el enlace conserva su href.
(function () {
    function leer(clave) { try { return sessionStorage.getItem(clave); } catch (e) { return null; } }
    function guardar(clave, valor) { try { sessionStorage.setItem(clave, valor); } catch (e) { } }
    function quitar(clave) { try { sessionStorage.removeItem(clave); } catch (e) { } }
    function ruta(url) { return url.split(/[?#]/)[0]; }

    var aqui = location.pathname;
    var anterior = leer('pf-ultima');
    var enviado = leer('pf-envio');
    quitar('pf-envio');
    if (anterior && ruta(anterior) !== aqui) {
        var origenDeAnterior = leer('pf-origen:' + ruta(anterior));
        if (origenDeAnterior && ruta(origenDeAnterior) === aqui) {
            // Regreso desde una página que se abrió desde esta (p. ej. de Asignar al pedido): se conserva su origen
        } else if (enviado === ruta(anterior)) {
            // Se llegó guardando un formulario (p. ej. aprobar una solicitud y pasar a la siguiente): hereda el origen
            if (origenDeAnterior) guardar('pf-origen:' + aqui, origenDeAnterior); else quitar('pf-origen:' + aqui);
        } else {
            guardar('pf-origen:' + aqui, anterior);
        }
    }

    document.querySelectorAll('a[data-pf-volver]').forEach(function (a) {
        var destino = a.dataset.pfVolver === 'ultima'
            ? leer('pf-visita:' + ruta(a.getAttribute('href')))
            : leer('pf-origen:' + aqui);
        if (destino && destino.charAt(0) === '/' && destino.charAt(1) !== '/') a.setAttribute('href', destino);
    });

    document.addEventListener('submit', function (e) { if (!e.defaultPrevented) guardar('pf-envio', location.pathname); });
    // Al salir se guarda la URL tal como quedó (página de la lista, filtros y búsqueda que cambian sin recargar)
    window.addEventListener('pagehide', function () {
        var url = location.pathname + location.search + location.hash;
        guardar('pf-ultima', url);
        guardar('pf-visita:' + location.pathname, url);
    });
})();

// Cuadro de búsqueda de una tabla: <input data-pf-buscar-en="idTabla">. Filtra las filas por todo su texto, sin
// distinguir tildes ni mayúsculas ("andres" encuentra "Andrés"), muestra la fila [data-pf-sin-resultados] si ninguna
// coincide y vuelve a paginar desde la primera página.
(function () {
    function normalizar(texto) {
        return texto.normalize('NFD').replace(/[̀-ͯ]/g, '').replace(/\s+/g, ' ').toLowerCase();
    }

    document.querySelectorAll('[data-pf-buscar-en]').forEach(function (campo) {
        var tabla = document.getElementById(campo.dataset.pfBuscarEn);
        if (!tabla || !tabla.tBodies[0]) return;
        var sinResultados = tabla.querySelector('[data-pf-sin-resultados]');
        // Las filas de "no hay registros" (una sola celda con colspan) no se filtran
        var filas = Array.prototype.filter.call(tabla.tBodies[0].rows, function (f) {
            return f !== sinResultados && !f.querySelector('td[colspan]');
        });
        filas.forEach(function (f) { f.dataset.pfTexto = normalizar(f.textContent); });

        campo.addEventListener('input', function () {
            var buscado = normalizar(campo.value.trim());
            var visibles = 0;
            filas.forEach(function (f) {
                var coincide = f.dataset.pfTexto.indexOf(buscado) !== -1;
                f.classList.toggle('d-none', !coincide);
                if (coincide) visibles++;
            });
            if (sinResultados) sinResultados.classList.toggle('d-none', visibles > 0);
            tabla.dispatchEvent(new CustomEvent('pf:repaginar', { detail: { inicio: true } }));
        });
    });
})();

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

        // data-pf-mostrar="texto" (inicio de sesión): el botón dice "Mostrar"/"Ocultar" en vez del ojito
        var conTexto = input.dataset.pfMostrar === 'texto';

        var btn = document.createElement('button');
        btn.type = 'button';
        btn.className = 'pf-password-toggle' + (conTexto ? ' pf-password-toggle--texto' : '');
        btn.setAttribute('aria-label', 'Mostrar contraseña');
        btn.innerHTML = conTexto ? 'Mostrar' : '<i class="bi bi-eye" aria-hidden="true"></i>';
        wrapper.appendChild(btn);

        btn.addEventListener('click', function () {
            var mostrar = input.type === 'password';
            input.type = mostrar ? 'text' : 'password';
            btn.setAttribute('aria-label', mostrar ? 'Ocultar contraseña' : 'Mostrar contraseña');

            if (conTexto) {
                btn.textContent = mostrar ? 'Ocultar' : 'Mostrar';
                input.focus({ preventScroll: true });
                return;
            }

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

// Píldora de la barra superior: un solo fondo que se desliza de la opción anterior a la nueva al cambiar de
// apartado. Sale hacia la opción con el clic (sin retrasar la navegación) y la página siguiente la recibe
// donde quedó, guardada en sessionStorage, para terminar el recorrido. Sin JavaScript la opción activa
// conserva su propio fondo.
(function () {
    var nav = document.getElementById('pfTopnav');
    if (!nav) return;
    var CLAVE = 'pf-barra-pildora';
    var VIGENCIA_MS = 5000; // más tiempo entre páginas ya no es "cambiar de apartado"

    function activa() {
        return nav.querySelector('.pf-topnav-link[aria-current="page"], .pf-topnav-link.activo');
    }

    var pildora = document.createElement('span');
    pildora.className = 'pf-topnav-pildora';
    pildora.setAttribute('aria-hidden', 'true');
    nav.prepend(pildora);
    nav.classList.add('pf-con-pildora');

    // Posición de una opción dentro de la barra (las de grupo están dentro de su .dropdown)
    function medir(el) {
        var caja = el.getBoundingClientRect();
        return { x: caja.left - nav.getBoundingClientRect().left, ancho: caja.width };
    }

    function colocar(x, ancho, animar) {
        pildora.classList.toggle('pf-animando', animar && !pfReducirMovimiento());
        pildora.style.width = ancho + 'px';
        pildora.style.transform = 'translateX(' + x + 'px)';
        pildora.hidden = !ancho;
    }

    function irA(el, animar) {
        if (!el) { colocar(0, 0, false); return; }
        var m = medir(el);
        colocar(m.x, m.ancho, animar);
    }

    // Llegada a la página: desde la posición guardada (si se vino de la barra hace un momento) o directo a la activa
    var guardado = null;
    try { guardado = JSON.parse(sessionStorage.getItem(CLAVE) || 'null'); } catch (e) { }
    try { sessionStorage.removeItem(CLAVE); } catch (e) { }
    var destino = activa();
    if (guardado && Date.now() - guardado.t < VIGENCIA_MS && guardado.ancho && destino) {
        colocar(guardado.x, guardado.ancho, false);
        requestAnimationFrame(function () {
            requestAnimationFrame(function () { irA(destino, true); });
        });
    } else {
        irA(destino, false);
    }

    // La fuente Geist llega después del primer pintado y cambia el ancho de las opciones
    if (document.fonts) document.fonts.ready.then(function () { irA(activa(), false); });
    window.addEventListener('resize', function () { irA(activa(), false); });

    // Clic normal en una opción directa o en una opción de un panel: la píldora sale ya hacia ella
    document.addEventListener('click', function (e) {
        var link = e.target.closest('.pf-topnav > a.pf-topnav-link, .pf-panel-opcion');
        if (!link || e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return;
        var objetivo = link.classList.contains('pf-panel-opcion')
            ? link.closest('.dropdown').querySelector('.pf-topnav-link')
            : link;
        if (objetivo === activa()) return;
        irA(objetivo, true);
    });

    // Al salir se guarda dónde va la píldora (puede estar a mitad de camino)
    window.addEventListener('pagehide', function () {
        var caja = pildora.getBoundingClientRect();
        try {
            sessionStorage.setItem(CLAVE, JSON.stringify({ x: caja.left - nav.getBoundingClientRect().left, ancho: caja.width, t: Date.now() }));
        } catch (e) { }
    });

    // Volver con el botón Atrás restaura la página desde la caché tal como quedó al hacer clic
    window.addEventListener('pageshow', function (e) {
        if (!e.persisted) return;
        try { sessionStorage.removeItem(CLAVE); } catch (err) { }
        irA(activa(), true);
    });
})();

// Hoja de navegación en pantallas angostas: baja desde la barra; se cierra con Escape, tocando fuera
// o al pasar a una pantalla ancha.
(function () {
    var root = document.documentElement;
    var boton = document.getElementById('pfMenuMovil');
    var hoja = document.getElementById('pfHoja');
    var fondo = document.getElementById('pfHojaFondo');
    if (!boton || !hoja) return;

    function mostrar(abrir) {
        root.classList.toggle('pf-hoja-abierta', abrir);
        boton.setAttribute('aria-expanded', abrir ? 'true' : 'false');
        boton.setAttribute('aria-label', abrir ? 'Cerrar menú' : 'Abrir menú');
        if (abrir) {
            var actual = hoja.querySelector('[aria-current="page"]') || hoja.querySelector('a');
            if (actual) actual.focus({ preventScroll: true });
        }
    }

    boton.addEventListener('click', function () { mostrar(!root.classList.contains('pf-hoja-abierta')); });
    if (fondo) fondo.addEventListener('click', function () { mostrar(false); });
    document.addEventListener('keydown', function (e) {
        if (e.key === 'Escape' && root.classList.contains('pf-hoja-abierta')) {
            mostrar(false);
            boton.focus();
        }
    });
    window.matchMedia('(min-width: 1021px)').addEventListener('change', function (m) {
        if (m.matches) mostrar(false);
    });
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
            headers: { 'X-Requested-With': 'fetch' },
            credentials: 'same-origin'
        }).then(function (r) {
            if (!r.ok) throw new Error('HTTP ' + r.status);
        });

        Promise.all([peticion, pausa]).then(function () {
            // Conserva el foco del teclado en la siguiente notificación (o la anterior)
            var vecino = item.nextElementSibling || item.previousElementSibling;
            var teniaFoco = item.contains(document.activeElement);
            return descartar(item).then(function () {
                lista.dispatchEvent(new Event('pf:repaginar'));
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

// Rebote elástico al llegar al inicio o al final de la página,
// como en iOS y macOS: el contenido se estira con resistencia y vuelve con un resorte suave.
// La capa recibe .pf-rebotando y --pf-rebote; el CSS mueve sus hijos con "translate".
(function () {
    var ESTIRAMIENTO_MAX = 100;   // px a los que tiende el estiramiento por mucho que se empuje
    var RESISTENCIA = 0.55;       // la misma constante del rebote de iOS
    var FACTOR_RUEDA = 0.35;      // un paso de la rueda del mouse estira unos 18 px
    var SOLTAR_TRAS_MS = 70;      // sin más movimiento de la rueda, el contenido vuelve
    var MAX_ESTIRADO_MS = 160;    // vuelve a más tardar a este tiempo, aunque la inercia del trackpad siga empujando
    var PAUSA_INERCIA_MS = 180;   // la inercia del trackpad que sigue llegando al soltar no vuelve a estirar
    // Rigidez de los resortes: más alta = más rápido. La vuelta tarda unos 0,3 s.
    var RIGIDEZ_EMPUJE = 900;
    var RIGIDEZ_VUELTA = 420;

    // Cuanto más se empuja, menos se mueve: nunca pasa de ESTIRAMIENTO_MAX
    function elastico(distancia) {
        var d = Math.abs(distancia);
        var x = (1 - 1 / (d * RESISTENCIA / ESTIRAMIENTO_MAX + 1)) * ESTIRAMIENTO_MAX;
        return distancia < 0 ? -x : x;
    }

    // Inversa de elastico(): retoma el estiramiento desde donde va la animación
    function distanciaDe(x) {
        var r = Math.min(Math.abs(x), ESTIRAMIENTO_MAX - 1) / ESTIRAMIENTO_MAX;
        var d = (1 / (1 - r) - 1) * ESTIRAMIENTO_MAX / RESISTENCIA;
        return x < 0 ? -d : d;
    }

    function pasoRueda(e) {
        return e.deltaY * (e.deltaMode === 1 ? 16 : e.deltaMode === 2 ? window.innerHeight : 1);
    }

    // zona: { capa, enInicio(), enFinal(), desplazable() }. Devuelve los manejadores de rueda y toque.
    function crearRebote(zona) {
        var distancia = 0;        // lo que se ha empujado más allá del borde (+ arriba, − abajo)
        var posicion = 0;         // desplazamiento que se ve
        var velocidad = 0;
        var objetivo = 0;
        var soltado = true;
        var cuadro = 0;
        var tiempoAnterior = 0;
        var temporizador = 0;
        var ignorarInercia = false;
        var ultimaRueda = 0;
        var inicioEstirado = 0;
        var ultimoY = 0;

        function pintar() {
            if (posicion === 0) {
                zona.capa.classList.remove('pf-rebotando');
                zona.capa.style.removeProperty('--pf-rebote');
                return;
            }
            zona.capa.classList.add('pf-rebotando');
            zona.capa.style.setProperty('--pf-rebote', posicion.toFixed(2) + 'px');
        }

        // Resorte con amortiguación crítica: llega sin oscilar. Al soltar es más suave que mientras se empuja.
        function animar(tiempo) {
            var dt = Math.min((tiempo - tiempoAnterior) / 1000, 0.032);
            tiempoAnterior = tiempo;
            var rigidez = soltado ? RIGIDEZ_VUELTA : RIGIDEZ_EMPUJE;
            var aceleracion = -rigidez * (posicion - objetivo) - 2 * Math.sqrt(rigidez) * velocidad;
            velocidad += aceleracion * dt;
            posicion += velocidad * dt;

            if (soltado && Math.abs(posicion) < 0.3 && Math.abs(velocidad) < 5) {
                posicion = 0;
                velocidad = 0;
                cuadro = 0;
                pintar();
                return;
            }
            pintar();
            cuadro = requestAnimationFrame(animar);
        }

        function arrancar() {
            if (cuadro) return;
            tiempoAnterior = performance.now();
            cuadro = requestAnimationFrame(animar);
        }

        function soltar() {
            clearTimeout(temporizador);
            distancia = 0;
            objetivo = 0;
            soltado = true;
            if (posicion !== 0) arrancar();
        }

        function rueda(e) {
            if (e.ctrlKey || Math.abs(e.deltaX) > Math.abs(e.deltaY) || pfReducirMovimiento()) return;
            var dy = pasoRueda(e);
            if (dy === 0) return;

            var ahora = performance.now();
            var pausa = ahora - ultimaRueda;
            ultimaRueda = ahora;
            if (ignorarInercia && pausa < PAUSA_INERCIA_MS) return;
            ignorarInercia = false;

            var empujaBorde = dy < 0 ? zona.enInicio() : zona.enFinal();
            if (!empujaBorde || !zona.desplazable()) {
                if (!soltado) soltar();
                return;
            }

            if (soltado) {
                distancia = distanciaDe(posicion);
                inicioEstirado = ahora;
            }
            soltado = false;
            distancia = dy < 0 ? Math.max(0, distancia) - dy * FACTOR_RUEDA : Math.min(0, distancia) - dy * FACTOR_RUEDA;
            objetivo = elastico(distancia);
            arrancar();

            // Vuelve al dejar de mover la rueda, o al cumplir MAX_ESTIRADO_MS aunque la inercia siga llegando
            clearTimeout(temporizador);
            temporizador = setTimeout(function () {
                soltar();
                ignorarInercia = true;
            }, Math.max(0, Math.min(SOLTAR_TRAS_MS, inicioEstirado + MAX_ESTIRADO_MS - ahora)));
        }

        function inicioToque(e) {
            if (e.touches.length !== 1) return;
            ultimoY = e.touches[0].clientY;
            distancia = distanciaDe(posicion);
        }

        // Mientras el dedo tira más allá del borde, el contenido lo sigue con resistencia
        // (se cancela el desplazamiento nativo); en cuanto vuelve, el navegador desplaza como siempre.
        function moverToque(e) {
            if (e.touches.length !== 1 || pfReducirMovimiento() || !zona.desplazable()) return;
            var y = e.touches[0].clientY;
            var dy = y - ultimoY;
            ultimoY = y;

            if (distancia > 0 || (distancia === 0 && dy > 0 && zona.enInicio())) {
                distancia = Math.max(0, distancia + dy);
            } else if (distancia < 0 || (distancia === 0 && dy < 0 && zona.enFinal())) {
                distancia = Math.min(0, distancia + dy);
            } else {
                return;
            }
            if (e.cancelable) e.preventDefault();

            cancelAnimationFrame(cuadro);
            cuadro = 0;
            soltado = false;
            posicion = objetivo = elastico(distancia);
            velocidad = 0;
            pintar();
        }

        return { rueda: rueda, inicioToque: inicioToque, moverToque: moverToque, soltar: soltar };
    }

    // Página: solo con rueda o trackpad. En pantallas táctiles el navegador ya tiene su propio efecto
    // (y deslizar hacia abajo para recargar), así que no se reemplaza.
    var pagina = document.querySelector('.pf-main-wrapper');
    if (pagina) {
        var raiz = document.scrollingElement || document.documentElement;
        var zonaPagina = {
            capa: pagina,
            enInicio: function () { return raiz.scrollTop <= 0; },
            enFinal: function () { return raiz.scrollTop + window.innerHeight >= raiz.scrollHeight - 1; },
            desplazable: function () { return raiz.scrollHeight > window.innerHeight + 1; }
        };
        var rebotePagina = crearRebote(zonaPagina);

        // La rueda sobre una zona con su propio desplazamiento (tablas, textos largos...) no mueve la página
        function dentroDeOtraZona(el) {
            for (; el && el !== document.body && el !== document.documentElement; el = el.parentElement) {
                var overflow = getComputedStyle(el).overflowY;
                if ((overflow === 'auto' || overflow === 'scroll') && el.scrollHeight > el.clientHeight + 1) return true;
            }
            return false;
        }

        window.addEventListener('wheel', function (e) {
            // Con un diálogo abierto la página no se desplaza y el diálogo se movería con ella
            if (document.body.classList.contains('modal-open')) return;
            // Recorrer el DOM (getComputedStyle) solo cuando la página ya está en el borde hacia el que se empuja
            var empujaBorde = pasoRueda(e) < 0 ? zonaPagina.enInicio() : zonaPagina.enFinal();
            if (empujaBorde && dentroDeOtraZona(e.target)) return;
            rebotePagina.rueda(e);
        }, { passive: true });
    }
})();

// Apariencia (Sistema / Claro / Oscuro) en el menú de la cuenta. La elección se guarda en este navegador;
// con "Sistema" se sigue el tema del equipo, también si cambia con la página abierta. El <head> ya aplicó
// data-theme antes de pintar (Views/Shared/_Cabecera.cshtml); aquí solo se cambia y se avisa con "pf-tema"
// para que las gráficas vuelvan a dibujarse con los colores nuevos.
(function () {
    var root = document.documentElement;
    var grupo = document.getElementById('pfTema');
    var sistemaOscuro = window.matchMedia('(prefers-color-scheme: dark)');
    var ORDEN = ['sistema', 'claro', 'oscuro'];

    function guardado() {
        var t = null;
        try { t = localStorage.getItem('pf-tema'); } catch (e) { }
        return t === 'claro' || t === 'oscuro' ? t : 'sistema';
    }

    function aplicar(eleccion) {
        var oscuro = eleccion === 'oscuro' || (eleccion === 'sistema' && sistemaOscuro.matches);
        var nuevo = oscuro ? 'dark' : 'light';
        if (root.getAttribute('data-theme') !== nuevo) {
            root.setAttribute('data-theme', nuevo);
            document.dispatchEvent(new CustomEvent('pf-tema', { detail: { oscuro: oscuro } }));
        }
        if (!grupo) return;
        grupo.style.setProperty('--pf-tema-i', ORDEN.indexOf(eleccion));
        grupo.querySelectorAll('[data-tema]').forEach(function (b) {
            b.setAttribute('aria-checked', b.dataset.tema === eleccion ? 'true' : 'false');
        });
    }

    aplicar(guardado());

    sistemaOscuro.addEventListener('change', function () {
        if (guardado() === 'sistema') aplicar('sistema');
    });

    if (!grupo) return;
    grupo.addEventListener('click', function (e) {
        var b = e.target.closest('[data-tema]');
        if (!b) return;
        e.stopPropagation(); // el menú de la cuenta sigue abierto para ver el cambio
        try {
            if (b.dataset.tema === 'sistema') localStorage.removeItem('pf-tema');
            else localStorage.setItem('pf-tema', b.dataset.tema);
        } catch (err) { }
        aplicar(b.dataset.tema);
    });

    // Radios con flechas, como un control segmentado nativo
    grupo.addEventListener('keydown', function (e) {
        if (e.key !== 'ArrowLeft' && e.key !== 'ArrowRight') return;
        e.preventDefault();
        var botones = Array.prototype.slice.call(grupo.querySelectorAll('[data-tema]'));
        var i = botones.indexOf(document.activeElement);
        var sig = botones[(i + (e.key === 'ArrowRight' ? 1 : botones.length - 1)) % botones.length];
        sig.focus();
        sig.click();
    });
})();

// Aviso flotante (TempData de éxito): sube desde abajo, se queda unos segundos y baja por el mismo camino.
// Si el puntero o el foco están encima, espera; el botón lo cierra antes.
(function () {
    var aviso = document.getElementById('pfAviso');
    if (!aviso) return;
    var temporizador = null;

    function cerrar() {
        aviso.classList.add('pf-aviso--oculto');
        window.setTimeout(function () { aviso.remove(); }, 450);
    }

    function programar() {
        window.clearTimeout(temporizador);
        temporizador = window.setTimeout(cerrar, 5000);
    }

    requestAnimationFrame(function () {
        requestAnimationFrame(function () { aviso.classList.remove('pf-aviso--oculto'); });
    });
    programar();

    aviso.addEventListener('mouseenter', function () { window.clearTimeout(temporizador); });
    aviso.addEventListener('mouseleave', programar);
    aviso.addEventListener('focusin', function () { window.clearTimeout(temporizador); });
    aviso.addEventListener('focusout', programar);
    aviso.querySelector('[data-pf-aviso-cerrar]').addEventListener('click', cerrar);
})();

// Paleta de comandos: Ctrl K (o Cmd K) o el botón "Buscar" de la barra. Las opciones salen de la
// navegación, así cada rol solo ve los apartados que puede abrir. Se abre y se cierra sin animación porque
// se usa muchas veces al día.
(function () {
    var paleta = document.getElementById('pfPaleta');
    var campo = document.getElementById('pfPaletaCampo');
    var lista = document.getElementById('pfPaletaLista');
    var boton = document.getElementById('pfBuscarBtn');
    if (!paleta || !campo || !lista) return;

    // Cada opción: { texto, grupo, url, icono (nodo svg clonado), nota, claves (texto para buscar) }.
    // Salen de la hoja de navegación (Views/Shared/_Layout.cshtml), que tiene todos los apartados del rol.
    var opciones = [];
    var grupoActual = '';
    var hoja = document.getElementById('pfHoja');
    if (hoja) {
        Array.prototype.forEach.call(hoja.children, function (el) {
            if (el.classList.contains('pf-hoja-grupo')) {
                grupoActual = el.textContent.trim();
                return;
            }
            if (!el.classList.contains('pf-hoja-link')) return;
            var texto = el.querySelector('.pf-hoja-texto').textContent.trim();
            var badge = el.querySelector('.pf-nav-badge');
            var svg = el.querySelector('svg');
            opciones.push({
                texto: texto,
                grupo: grupoActual || 'Apartados',
                url: el.href,
                icono: svg ? svg.cloneNode(true) : null,
                nota: badge ? badge.textContent.trim() + ' sin atender' : '',
                claves: (texto + ' ' + grupoActual).toLowerCase()
            });
        });
    }
    var cuenta = document.querySelector('.pf-cuenta-menu a.dropdown-item');
    if (cuenta) {
        opciones.push({
            texto: cuenta.textContent.trim(), grupo: 'Cuenta', url: cuenta.href,
            icono: cuenta.querySelector('svg') ? cuenta.querySelector('svg').cloneNode(true) : null,
            nota: '', claves: 'cambiar contraseña clave cuenta'
        });
    }

    // Sin tildes ni mayúsculas: "configuracion" encuentra "Configuración"
    function normalizar(t) {
        return t.toLowerCase().normalize('NFD').replace(/[̀-ͯ]/g, '');
    }

    var visibles = [];
    var elegido = 0;
    var anterior = null;

    function pintar() {
        var q = normalizar(campo.value.trim());
        visibles = opciones.filter(function (o) { return !q || normalizar(o.claves).indexOf(q) >= 0; });
        if (elegido >= visibles.length) elegido = 0;
        lista.replaceChildren();

        if (!visibles.length) {
            var vacio = document.createElement('li');
            vacio.className = 'pf-paleta-vacia';
            vacio.textContent = 'Nada coincide con «' + campo.value.trim() + '».';
            lista.appendChild(vacio);
            campo.removeAttribute('aria-activedescendant');
            return;
        }

        var ultimoGrupo = null;
        visibles.forEach(function (o, i) {
            if (o.grupo !== ultimoGrupo) {
                ultimoGrupo = o.grupo;
                var g = document.createElement('li');
                g.className = 'pf-paleta-grupo';
                g.setAttribute('role', 'presentation');
                g.textContent = o.grupo;
                lista.appendChild(g);
            }
            var li = document.createElement('li');
            li.className = 'pf-paleta-opcion';
            li.id = 'pfPaletaOpcion' + i;
            li.setAttribute('role', 'option');
            li.setAttribute('aria-selected', i === elegido ? 'true' : 'false');
            if (o.icono) li.appendChild(o.icono.cloneNode(true));
            var t = document.createElement('span');
            t.textContent = o.texto;
            li.appendChild(t);
            if (o.nota) {
                var n = document.createElement('span');
                n.className = 'pf-paleta-opcion-nota';
                n.textContent = o.nota;
                li.appendChild(n);
            }
            li.addEventListener('mousemove', function () {
                if (elegido !== i) { elegido = i; marcar(); }
            });
            li.addEventListener('click', function () { abrir(o); });
            lista.appendChild(li);
        });
        marcar();
    }

    function marcar() {
        lista.querySelectorAll('.pf-paleta-opcion').forEach(function (li, i) {
            li.setAttribute('aria-selected', i === elegido ? 'true' : 'false');
        });
        var actual = document.getElementById('pfPaletaOpcion' + elegido);
        if (actual) {
            campo.setAttribute('aria-activedescendant', actual.id);
            actual.scrollIntoView({ block: 'nearest' });
        }
    }

    function abrir(o) {
        window.location.href = o.url;
    }

    function mostrar() {
        if (!paleta.hidden) return;
        anterior = document.activeElement;
        paleta.hidden = false;
        campo.value = '';
        elegido = 0;
        pintar();
        campo.focus();
    }

    function ocultar() {
        if (paleta.hidden) return;
        paleta.hidden = true;
        if (anterior && anterior.focus) anterior.focus({ preventScroll: true });
    }

    if (boton) boton.addEventListener('click', mostrar);
    paleta.querySelectorAll('[data-pf-paleta-cerrar]').forEach(function (b) { b.addEventListener('click', ocultar); });
    campo.addEventListener('input', function () { elegido = 0; pintar(); });

    campo.addEventListener('keydown', function (e) {
        if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
            e.preventDefault();
            if (!visibles.length) return;
            elegido = (elegido + (e.key === 'ArrowDown' ? 1 : visibles.length - 1)) % visibles.length;
            marcar();
        } else if (e.key === 'Enter') {
            e.preventDefault();
            if (visibles[elegido]) abrir(visibles[elegido]);
        }
    });

    document.addEventListener('keydown', function (e) {
        if ((e.ctrlKey || e.metaKey) && !e.altKey && (e.key === 'k' || e.key === 'K')) {
            e.preventDefault();
            if (paleta.hidden) mostrar(); else ocultar();
        } else if (e.key === 'Escape' && !paleta.hidden) {
            e.preventDefault();
            ocultar();
        } else if (e.key === 'Tab' && !paleta.hidden) {
            // El foco queda dentro del diálogo: solo el campo y el botón Esc
            var foco = [campo, paleta.querySelector('.pf-paleta-campo button')];
            var i = foco.indexOf(document.activeElement);
            e.preventDefault();
            foco[(i + (e.shiftKey ? foco.length - 1 : 1)) % foco.length].focus();
        }
    });
})();
