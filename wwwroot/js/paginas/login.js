// Inicio de sesión sin recargar: el formulario se envía con fetch y AccountController.Login responde en JSON.
// Si hay un error, aparece el mensaje y el cuadro tiembla (como la ventana de inicio de sesión de macOS);
// si todo está bien, se navega al destino. Si fetch falla, el formulario se envía de la forma normal.
(function () {
    var form = document.getElementById('pfLoginForm');
    var tarjeta = document.querySelector('.pf-login-card');
    var aviso = document.getElementById('pfLoginAviso');
    var boton = form.querySelector('[type="submit"]');
    var textoBoton = boton.innerHTML;
    var campos = { Correo: form.elements['Correo'], Password: form.elements['Password'] };

    function temblar() {
        tarjeta.classList.remove('pf-temblor');
        void tarjeta.offsetWidth; // reinicia la animación si se equivoca varias veces seguidas
        tarjeta.classList.add('pf-temblor');
    }

    // errores: { "": [mensajes generales], "Correo": [...], "Password": [...] }; se escriben como texto, nunca como HTML
    function mostrarErrores(errores) {
        aviso.replaceChildren();
        (errores[''] || []).forEach(function (m) {
            var linea = document.createElement('div');
            linea.textContent = m;
            aviso.appendChild(linea);
        });
        aviso.hidden = !aviso.hasChildNodes();

        Object.keys(campos).forEach(function (nombre) {
            var span = form.querySelector('[data-valmsg-for="' + nombre + '"]');
            var mensajes = errores[nombre] || [];
            if (span) span.textContent = mensajes.join(' ');
            campos[nombre].classList.toggle('pf-login-input--error', mensajes.length > 0);
        });

        // Contraseña equivocada: se borra para escribirla de nuevo; si falta el correo, el foco va allí
        var foco = errores['Correo'] ? campos.Correo : campos.Password;
        if (!errores['Correo']) campos.Password.value = '';
        foco.focus();
    }

    Object.keys(campos).forEach(function (nombre) {
        campos[nombre].addEventListener('input', function () { campos[nombre].classList.remove('pf-login-input--error'); });
    });

    form.addEventListener('submit', async function (e) {
        e.preventDefault();
        if (boton.disabled) return;
        boton.disabled = true;
        boton.innerHTML = '<span class="pf-spinner" aria-hidden="true"></span>Iniciando sesión…';
        try {
            var r = await fetch(form.action, { method: 'POST', body: new FormData(form), headers: { 'X-Requested-With': 'fetch' } });
            // Demasiados intentos desde esta conexión (límite "InicioSesion" de Program.cs): el aviso va en el mismo cuadro
            if (r.status === 429) {
                mostrarErrores({ '': [await r.text()] });
                temblar();
                boton.disabled = false;
                boton.innerHTML = textoBoton;
                return;
            }
            if (!r.ok) throw new Error(r.status);
            var datos = await r.json();
            if (datos.ok) {
                window.location.assign(datos.url); // el botón sigue en "Iniciando sesión…" mientras carga
                return;
            }
            // Token antifalsificación vigente para el siguiente intento (por si el del formulario caducó)
            if (datos.token) form.elements['__RequestVerificationToken'].value = datos.token;
            mostrarErrores(datos.errores);
            temblar();
        } catch (err) {
            form.submit();
            return;
        }
        boton.disabled = false;
        boton.innerHTML = textoBoton;
    });
})();
