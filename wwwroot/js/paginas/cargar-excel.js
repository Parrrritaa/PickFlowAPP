(function () {
    var pagina = document.currentScript.dataset;
    var form = document.getElementById('formExcel');
    var input = document.getElementById('archivoExcel');
    var btnValidar = document.getElementById('btnValidar');
    var textoValidar = btnValidar.innerHTML;
    var seccion = document.getElementById('sectionValidacion');
    var lista = document.getElementById('listaVerificaciones');
    var resumen = document.getElementById('resumenValidacion');
    var cajaErrores = document.getElementById('erroresValidacion');
    var listaErrores = document.getElementById('listaErroresValidacion');
    var btnConfirmar = document.getElementById('btnConfirmarCarga');
    var textoConfirmar = btnConfirmar.innerHTML;
    var seccionIncidencias = document.getElementById('seccionIncidencias');
    var listaIncidencias = document.getElementById('listaIncidencias');
    var avisos = document.getElementById('avisosNoDisponibles');
    var listaNoDisponibles = document.getElementById('listaNoDisponibles');
    var urlValidar = pagina.urlValidar;
    var erroresVisiblesInicial = 5;
    // Clave de la incidencia → su tarjeta, para mostrar el error de cada decisión
    var tarjetas = {};
    var decisionesRevisadas = false;

    var caja = document.getElementById('cajaArchivo');
    var tituloArchivo = document.getElementById('tituloArchivo');
    var ayudaArchivo = document.getElementById('ayudaArchivo');
    var textoTitulo = tituloArchivo.textContent;
    var textoAyuda = ayudaArchivo.textContent;
    var textoElegir = document.getElementById('btnElegirArchivo').innerHTML;

    function tamano(bytes) {
        return bytes < 1024 * 1024 ? Math.max(1, Math.round(bytes / 1024)) + ' KB' : (bytes / 1024 / 1024).toFixed(1) + ' MB';
    }

    // La zona muestra el archivo elegido (nombre y tamaño) o la invitación a elegirlo
    function mostrarArchivo() {
        var archivo = input.files && input.files[0];
        tituloArchivo.textContent = archivo ? archivo.name : textoTitulo;
        ayudaArchivo.textContent = archivo ? tamano(archivo.size) + ' · listo para validar' : textoAyuda;
        ayudaArchivo.classList.remove('is-error');
        document.getElementById('btnElegirArchivo').innerHTML = archivo
            ? '<i class="bi bi-arrow-repeat me-1" aria-hidden="true"></i>Cambiar archivo'
            : textoElegir;
    }

    // Un archivo nuevo invalida el resultado anterior y la zona vuelve a su tamaño completo
    input.addEventListener('change', function () {
        seccion.hidden = true;
        btnConfirmar.hidden = true;
        listaIncidencias.innerHTML = '';
        tarjetas = {};
        caja.classList.remove('is-compacta');
        mostrarArchivo();
    });

    // Arrastrar y soltar el archivo sobre la zona equivale a elegirlo
    ['dragenter', 'dragover'].forEach(function (evento) {
        caja.addEventListener(evento, function (e) {
            e.preventDefault();
            caja.classList.add('is-arrastrando');
        });
    });
    ['dragleave', 'drop'].forEach(function (evento) {
        caja.addEventListener(evento, function (e) {
            e.preventDefault();
            if (evento === 'dragleave' && caja.contains(e.relatedTarget)) return;
            caja.classList.remove('is-arrastrando');
        });
    });
    caja.addEventListener('drop', function (e) {
        if (!e.dataTransfer.files.length) return;
        input.files = e.dataTransfer.files;
        input.dispatchEvent(new Event('change', { bubbles: true }));
    });

    function plural(n, singular, varios) { return n + ' ' + (n === 1 ? singular : varios); }

    function elemento(etiqueta, clase, texto) {
        var el = document.createElement(etiqueta);
        if (clase) el.className = clase;
        if (texto != null) el.textContent = texto;
        return el;
    }

    // "4, 7, 9, 12 y 3 más": evita tarjetas enormes cuando un producto aparece en muchas filas
    function listar(valores) {
        var max = 4;
        var visibles = valores.slice(0, max).join(', ');
        if (valores.length <= max) return valores.length > 1 ? visibles.replace(/, ([^,]*)$/, ' y $1') : visibles;
        return visibles + ' y ' + (valores.length - max) + ' más';
    }

    // estado: 'ok' | 'error' | 'revisar' | 'aviso' | 'pendiente'. Todo se escribe como texto: los mensajes incluyen contenido del archivo.
    function verificacion(estado, texto, detalle) {
        var li = document.createElement('li');
        li.className = 'd-flex align-items-start text-dark';
        var icono = document.createElement('i');
        icono.setAttribute('aria-hidden', 'true');
        icono.className = 'bi me-2 fs-5 ' + (estado === 'ok' ? 'bi-check-circle-fill text-success'
            : estado === 'error' ? 'bi-x-circle-fill text-danger'
            : estado === 'revisar' || estado === 'aviso' ? 'bi-exclamation-circle-fill pf-texto-revisar' : 'bi-dash-circle text-secondary');
        var cuerpo = document.createElement('span');
        var titulo = document.createElement('span');
        titulo.textContent = texto;
        var estadoSr = document.createElement('span');
        estadoSr.className = 'visually-hidden';
        estadoSr.textContent = estado === 'ok' ? ' (correcto)' : estado === 'error' ? ' (con errores)'
            : estado === 'revisar' ? ' (por decidir)' : estado === 'aviso' ? ' (con aviso)' : ' (sin verificar)';
        cuerpo.append(titulo, estadoSr);
        if (detalle) {
            var small = document.createElement('div');
            small.className = 'small text-secondary';
            small.textContent = detalle;
            cuerpo.appendChild(small);
        }
        li.append(icono, cuerpo);
        lista.appendChild(li);
    }

    function mostrar(r) {
        lista.innerHTML = '';
        listaErrores.innerHTML = '';
        lista.hidden = !!r.sinVerificar;

        var estructura = r.tipoValido && r.estructuraValida;
        verificacion(r.tipoValido ? 'ok' : 'error', 'Tipo de archivo válido');
        verificacion(!r.tipoValido ? 'pendiente' : r.estructuraValida ? 'ok' : 'error', 'Estructura de archivo válida');

        // Una fila con error no pasa a las verificaciones siguientes: esas quedan pendientes hasta corregirla.
        // Los pasos "decidir" y "aviso" no son errores del archivo: las filas por decidir se confirman abajo, en las
        // tarjetas, y las no disponibles no se cargan (su pedido queda con una novedad).
        // Mismo orden que ExcelService.ValidarArchivoExcelAsync.
        var pasos = [
            ['filasConCantidadInvalida', 'Cantidades válidas'],
            ['filasConBodegaNoRegistrada', 'Bodegas registradas']
        ];
        if (r.soloSuBodega) pasos.push(['filasDeOtraBodega', 'Pedidos de su bodega']);
        pasos.push(
            ['filasConProductoNoRegistrado', 'Productos registrados'],
            ['filasConPedidoDuplicado', 'Pedidos no registrados previamente'],
            ['filasNoDisponibles', 'Productos disponibles en la bodega del pedido', 'aviso'],
            ['filasConNombreDistinto', 'Nombres coinciden con el código', 'decidir']
        );
        var erroresPrevios = 0;
        pasos.forEach(function (p) {
            var filas = r[p[0]] || 0;
            var tipo = p[2];
            if (!estructura) {
                verificacion('pendiente', p[1]);
            } else if (filas > 0) {
                if (tipo === 'decidir') verificacion('revisar', p[1], plural(filas, 'fila por decidir', 'filas por decidir'));
                else if (tipo === 'aviso') verificacion('aviso', p[1], plural(filas, 'fila no se cargará', 'filas no se cargarán'));
                else verificacion('error', p[1], plural(filas, 'fila con error', 'filas con error'));
            } else if (erroresPrevios > 0) {
                verificacion('pendiente', p[1], 'Se verificará en las filas con errores cuando se corrijan');
            } else {
                verificacion('ok', p[1]);
            }
            if (!tipo) erroresPrevios += filas;
        });

        var errores = r.errores || [];
        var incidencias = r.incidencias || [];
        var filasPorDecidir = incidencias.reduce(function (total, inc) { return total + inc.filas.length; }, 0);

        if (r.sinVerificar) {
            resumen.textContent = 'No fue posible validar el archivo. Revise su conexión e inténtelo de nuevo.';
        } else if (r.valido) {
            resumen.textContent = plural(r.totalPedidos, 'pedido', 'pedidos') + ' (' +
                plural(r.totalFilas, 'línea de producto', 'líneas de producto') + ') listos para cargar.';
        } else if (incidencias.length && !errores.length) {
            resumen.textContent = plural(r.totalPedidos, 'pedido', 'pedidos') + ' listos para cargar cuando confirme el nombre de ' +
                plural(filasPorDecidir, 'fila', 'filas') + '.';
        } else if (incidencias.length) {
            resumen.textContent = 'El archivo no se puede cargar todavía. Cuando corrija los errores podrá confirmar el nombre de ' +
                plural(filasPorDecidir, 'fila', 'filas') + '.';
        } else {
            resumen.textContent = 'El archivo no se puede cargar todavía.';
        }

        errores.forEach(function (e, i) {
            var li = document.createElement('li');
            li.textContent = e;
            if (i >= erroresVisiblesInicial) li.classList.add('d-none', 'pf-error-extra');
            listaErrores.appendChild(li);
        });
        var ocultos = errores.length - erroresVisiblesInicial;
        if (ocultos > 0) {
            var liToggle = document.createElement('li');
            liToggle.className = 'mt-1';
            liToggle.style.listStyleType = 'none';
            var btnToggle = document.createElement('button');
            btnToggle.type = 'button';
            btnToggle.className = 'btn btn-link fw-semibold text-decoration-none p-0';
            var textoMas = 'Ver ' + plural(ocultos, 'observación más', 'observaciones más');
            btnToggle.textContent = textoMas;
            btnToggle.addEventListener('click', function () {
                var expandir = btnToggle.dataset.expandido !== 'true';
                var items = Array.prototype.slice.call(listaErrores.querySelectorAll('.pf-error-extra'));
                btnToggle.disabled = true;
                btnToggle.dataset.expandido = expandir ? 'true' : 'false';
                btnToggle.textContent = expandir ? 'Ver menos' : textoMas;

                if (expandir) {
                    // Al desplegar, cada fila aparece con un pequeño desfase (cascada) en vez de todas a la vez
                    items.forEach(function (el, idx) {
                        el.classList.remove('d-none');
                        el.classList.remove('pf-reveal');
                        if (pfReducirMovimiento()) return;
                        el.style.animationDelay = (idx * 30) + 'ms';
                        void el.offsetWidth;
                        el.classList.add('pf-reveal');
                    });
                    btnToggle.disabled = false;
                    return;
                }

                // Al ocultar, las filas se recogen hacia arriba con un fundido: más rápido que la
                // entrada y sin desfase notorio, para que "Ver menos" se sienta ágil, no perezoso.
                if (pfReducirMovimiento() || !items[0] || !items[0].animate) {
                    items.forEach(function (el) { el.classList.add('d-none'); });
                    btnToggle.disabled = false;
                    return;
                }
                var salidas = items.map(function (el, idx) {
                    return el.animate(
                        [{ opacity: 1, transform: 'none' }, { opacity: 0, transform: 'translateY(-6px)' }],
                        { duration: 150, easing: 'cubic-bezier(0.4, 0, 0.2, 1)', delay: idx * 12, fill: 'forwards' }
                    ).finished;
                });
                Promise.all(salidas).then(function () {
                    items.forEach(function (el) {
                        el.classList.add('d-none');
                        el.style.opacity = '';
                        el.style.transform = '';
                    });
                    btnToggle.disabled = false;
                });
            });
            liToggle.appendChild(btnToggle);
            listaErrores.appendChild(liToggle);
        }
        cajaErrores.hidden = errores.length === 0;

        var noDisponibles = r.noDisponibles || [];
        listaNoDisponibles.innerHTML = '';
        noDisponibles.forEach(function (t) { listaNoDisponibles.appendChild(elemento('li', null, t)); });
        avisos.hidden = noDisponibles.length === 0;

        // Las decisiones solo se piden cuando el archivo no tiene errores: al corregirlo se vuelve a validar desde cero
        listaIncidencias.innerHTML = '';
        tarjetas = {};
        decisionesRevisadas = false;
        var decidir = incidencias.length > 0 && errores.length === 0;
        if (decidir) {
            incidencias.forEach(function (inc, i) {
                var t = tarjeta(inc, i);
                tarjetas[inc.clave] = t;
                listaIncidencias.appendChild(t);
            });
        }
        seccionIncidencias.hidden = !decidir;
        btnConfirmar.hidden = !(r.valido || decidir);

        seccion.hidden = false;
        caja.classList.add('is-compacta');
        if (input.files && input.files[0]) {
            ayudaArchivo.textContent = tamano(input.files[0].size) + ' · ' + (r.valido ? 'listo para cargar'
                : r.sinVerificar ? 'no se pudo validar' : 'revise el resultado');
        }
        seccion.classList.remove('pf-reveal');
        void seccion.offsetWidth;
        seccion.classList.add('pf-reveal');
    }

    // Tarjeta de un nombre distinto al registrado. Los campos se envían como resoluciones[i].* (ResolucionExcel en el servidor).
    function tarjeta(inc, i) {
        var prefijo = 'resoluciones[' + i + '].';
        var id = 'incidencia' + i;

        var card = elemento('section', 'pf-incidencia');
        card.setAttribute('aria-labelledby', id + '-titulo');

        var clave = elemento('input');
        clave.type = 'hidden';
        clave.name = prefijo + 'Clave';
        clave.value = inc.clave;
        card.appendChild(clave);

        var cabecera = elemento('div', 'pf-incidencia-cabecera');
        var icono = elemento('span', 'pf-incidencia-icono');
        icono.setAttribute('aria-hidden', 'true');
        icono.appendChild(elemento('i', 'bi bi-tag'));
        var textos = elemento('div');
        var titulo = elemento('h6', 'pf-incidencia-titulo', inc.titulo);
        titulo.id = id + '-titulo';
        textos.append(titulo, elemento('p', 'pf-incidencia-meta',
            (inc.filas.length === 1 ? 'Fila ' : 'Filas ') + listar(inc.filas) + ' · ' +
            (inc.pedidos.length === 1 ? 'Pedido ' : 'Pedidos ') + listar(inc.pedidos)));
        cabecera.append(icono, textos);
        card.append(cabecera, elemento('p', 'pf-incidencia-texto', inc.detalle));

        var opciones = elemento('fieldset', 'pf-opciones');
        opciones.appendChild(elemento('legend', 'pf-label', '¿Con qué nombre se carga?'));
        var lista = elemento('div', 'pf-opciones-lista');
        // Un nombre distinto se confirma siempre a mano: la única opción no se deja elegida
        lista.appendChild(opcion(prefijo, 'nombre-registrado', 'Cargar como ' + inc.codigoProducto + ' · ' + inc.nombreProducto,
            'Se usa el nombre registrado en el sistema'));
        opciones.appendChild(lista);
        card.appendChild(opciones);

        var error = elemento('p', 'pf-incidencia-error text-danger');
        error.setAttribute('role', 'alert');
        error.hidden = true;
        card.appendChild(error);
        card.addEventListener('change', function () { error.hidden = true; });

        card._error = error;
        return card;
    }

    function opcion(prefijo, valor, texto, nota) {
        var fila = elemento('div', 'pf-opcion-fila');
        var label = elemento('label', 'pf-opcion');
        var radio = elemento('input', 'form-check-input');
        radio.type = 'radio';
        radio.name = prefijo + 'Opcion';
        radio.value = valor;
        radio.required = true;
        var cuerpo = elemento('span');
        cuerpo.append(elemento('span', 'pf-opcion-titulo', texto), elemento('span', 'pf-opcion-nota', nota));
        label.append(radio, cuerpo);
        fila.appendChild(label);
        return fila;
    }

    function ponerCargando(texto) {
        btnConfirmar.classList.add('is-loading');
        btnConfirmar.innerHTML = '<span class="pf-spinner" aria-hidden="true"></span>' + texto;
    }

    function quitarCargando() {
        btnConfirmar.classList.remove('is-loading');
        btnConfirmar.innerHTML = textoConfirmar;
    }

    // Antes de guardar, el servidor revisa las decisiones con el mismo archivo (sin registrar nada).
    // Si alguna no sirve se muestra en su tarjeta.
    function revisarDecisiones() {
        ponerCargando('Revisando…');
        Object.keys(tarjetas).forEach(function (c) { tarjetas[c]._error.hidden = true; });

        fetch(urlValidar, { method: 'POST', body: new FormData(form) })
            .then(function (resp) {
                if (!resp.ok) throw new Error();
                return resp.json();
            })
            .then(function (r) {
                if (r.valido) {
                    decisionesRevisadas = true;
                    ponerCargando('Cargando pedidos…');
                    form.submit();
                    return;
                }

                var primera = null;
                var generales = (r.errores || []).slice();
                Object.keys(r.erroresResolucion || {}).forEach(function (c) {
                    var t = tarjetas[c];
                    if (!t) return;
                    t._error.textContent = r.erroresResolucion[c];
                    t._error.hidden = false;
                    primera = primera || t;
                });
                // Incidencias que no estaban al validar: algo cambió en el sistema mientras tanto
                if ((!primera && !generales.length) || (r.incidencias || []).some(function (inc) { return !tarjetas[inc.clave]; })) {
                    generales.push('Los productos cambiaron desde la validación. Vuelva a validar el archivo.');
                }
                listaErrores.innerHTML = '';
                generales.forEach(function (e) { listaErrores.appendChild(elemento('li', null, e)); });
                cajaErrores.hidden = generales.length === 0;

                quitarCargando();
                var destino = primera || (generales.length ? cajaErrores : null);
                if (destino) destino.scrollIntoView({ block: 'center', behavior: pfReducirMovimiento() ? 'auto' : 'smooth' });
            })
            .catch(function () {
                listaErrores.innerHTML = '';
                listaErrores.appendChild(elemento('li', null, 'No fue posible revisar las decisiones. Revise su conexión e inténtelo de nuevo.'));
                cajaErrores.hidden = false;
                quitarCargando();
            });
    }

    btnValidar.addEventListener('click', function () {
        if (!input.files || input.files.length === 0) {
            // El selector nativo está oculto: el aviso va en la misma zona
            ayudaArchivo.textContent = 'Primero elige el archivo Excel que quieres validar.';
            ayudaArchivo.classList.add('is-error');
            input.focus();
            return;
        }

        var datos = new FormData();
        datos.append('archivoExcel', input.files[0]);
        datos.append('__RequestVerificationToken', form.querySelector('input[name="__RequestVerificationToken"]').value);

        btnValidar.classList.add('is-loading');
        btnValidar.innerHTML = '<span class="pf-spinner" aria-hidden="true"></span>Validando…';

        fetch(pagina.urlValidar, { method: 'POST', body: datos })
            .then(function (resp) {
                if (!resp.ok) throw new Error();
                return resp.json();
            })
            .then(mostrar)
            .catch(function () {
                mostrar({ valido: false, sinVerificar: true });
            })
            .finally(function () {
                btnValidar.classList.remove('is-loading');
                btnValidar.innerHTML = textoValidar;
            });
    });

    form.addEventListener('submit', function (e) {
        if (btnConfirmar.classList.contains('is-loading')) {
            e.preventDefault();
            return;
        }
        if (listaIncidencias.children.length && !decisionesRevisadas) {
            e.preventDefault();
            revisarDecisiones();
            return;
        }
        // Evita un doble envío mientras el servidor registra los pedidos
        ponerCargando('Cargando pedidos…');
    });
})();
