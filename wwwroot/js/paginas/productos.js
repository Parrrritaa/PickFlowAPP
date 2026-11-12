(function () {
    var nombrePredeterminada = document.currentScript.dataset.nombrePredeterminada;

    // Diálogo de categoría: el mismo formulario sirve para crear y editar
    var modalCategoria = document.getElementById('modalCategoria');
    modalCategoria.addEventListener('show.bs.modal', function (e) {
        var origen = e.relatedTarget;
        var editar = origen && origen.dataset.modo === 'editar';
        document.getElementById('tituloModalCategoria').textContent = editar ? 'Editar categoría' : 'Nueva categoría';
        document.getElementById('categoriaId').value = editar ? origen.dataset.id : '';
        document.getElementById('categoriaNombre').value = editar ? origen.dataset.nombre : '';
        document.getElementById('categoriaMinutos').value = editar ? origen.dataset.minutos : '';
        document.getElementById('categoriaDescripcion').value = editar ? (origen.dataset.descripcion || '') : '';
    });
    modalCategoria.addEventListener('shown.bs.modal', function () {
        document.getElementById('categoriaNombre').focus();
    });

    // Diálogo de producto: el código no cambia; el nombre aplica en todas sus ubicaciones
    var modalProducto = document.getElementById('modalProducto');
    modalProducto.addEventListener('show.bs.modal', function (e) {
        var origen = e.relatedTarget;
        var ubicaciones = Number.parseInt(origen.dataset.ubicaciones, 10) || 0;
        document.getElementById('tituloModalProducto').textContent = 'Editar ' + origen.dataset.codigo;
        document.getElementById('textoModalProducto').textContent = ubicaciones > 1
            ? 'Los cambios se verán en sus ' + ubicaciones + ' ubicaciones.'
            : 'El código del producto no cambia.';
        document.getElementById('productoId').value = origen.dataset.id;
        document.getElementById('productoNombre').value = origen.dataset.nombre;
        document.getElementById('productoUnidades').value = origen.dataset.unidades;
    });
    modalProducto.addEventListener('shown.bs.modal', function () {
        document.getElementById('productoNombre').focus();
    });

    var modalEliminar = document.getElementById('modalEliminarCategoria');
    modalEliminar.addEventListener('show.bs.modal', function (e) {
        var origen = e.relatedTarget;
        var n = Number.parseInt(origen.dataset.productos, 10) || 0;
        document.getElementById('eliminarCategoriaId').value = origen.dataset.id;
        document.getElementById('tituloEliminarCategoria').textContent = '¿Eliminar «' + origen.dataset.nombre + '»?';
        document.getElementById('textoEliminarCategoria').textContent = n === 0
            ? 'No tiene productos asignados.'
            : (n === 1 ? 'Su producto pasará' : 'Sus ' + n + ' productos pasarán') + ' a usar «' + nombrePredeterminada + '», la categoría por defecto.';
    });

    // Clasificación de productos: cambios pendientes, filtro y aplicación en lote
    var form = document.getElementById('formClasificacion');
    var tabla = document.getElementById('tablaProductos');
    if (!tabla) return;
    var selects = Array.prototype.slice.call(tabla.querySelectorAll('.pf-select-categoria'));
    var btnGuardar = document.getElementById('btnGuardarClasificacion');
    var textoCambios = document.getElementById('textoCambios');
    var buscar = document.getElementById('buscarProducto');
    var filtroCategoria = document.getElementById('filtroCategoria');
    var sinProductos = document.getElementById('filaSinProductos');
    var enviando = false;

    function pendientes() {
        return selects.filter(function (s) { return s.value !== (s.dataset.original || ''); }).length;
    }

    function actualizarCambios() {
        selects.forEach(function (s) {
            s.closest('tr').classList.toggle('pf-fila-modificada', s.value !== (s.dataset.original || ''));
        });
        var n = pendientes();
        btnGuardar.disabled = n === 0;
        btnGuardar.textContent = n === 0 ? 'Guardar cambios' : (n === 1 ? 'Guardar 1 cambio' : 'Guardar ' + n + ' cambios');
        textoCambios.textContent = n === 0 ? 'Sin cambios pendientes' : (n === 1 ? '1 producto modificado' : n + ' productos modificados');
    }

    function filasVisibles() {
        return selects.map(function (s) { return s.closest('tr'); }).filter(function (f) { return !f.classList.contains('d-none'); });
    }

    function filtrar() {
        var t = buscar.value.trim().toLowerCase();
        var cat = filtroCategoria.value;
        var visibles = 0;
        selects.forEach(function (s) {
            var fila = s.closest('tr');
            var coincideCat = cat === '' || (cat === '__defecto' ? s.value === '' : s.value === cat);
            var mostrar = fila.dataset.buscar.indexOf(t) !== -1 && coincideCat;
            fila.classList.toggle('d-none', !mostrar);
            if (mostrar) visibles++;
        });
        sinProductos.classList.toggle('d-none', visibles > 0);
        tabla.dispatchEvent(new CustomEvent('pf:repaginar', { detail: { inicio: true } }));
    }

    tabla.addEventListener('change', function (e) {
        if (e.target.classList.contains('pf-select-categoria')) actualizarCambios();
    });
    buscar.addEventListener('input', filtrar);
    filtroCategoria.addEventListener('change', filtrar);

    document.getElementById('btnAplicarLote').addEventListener('click', function () {
        var valor = document.getElementById('categoriaLote').value;
        filasVisibles().forEach(function (f) {
            var s = f.querySelector('.pf-select-categoria');
            s.value = valor;
        });
        actualizarCambios();
    });

    form.addEventListener('submit', function () { enviando = true; });

    // Evita perder la clasificación si se sale de la página sin guardar
    window.addEventListener('beforeunload', function (e) {
        if (!enviando && pendientes() > 0) {
            e.preventDefault();
            e.returnValue = '';
        }
    });

    actualizarCambios();
})();
