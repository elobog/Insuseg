// Expuesto como InsusegTablas.aplicarBotonExpandir para que el contenido cargado por AJAX
// (ej. el detalle por producto de Ventas/Cartera) pueda pedir el mismo botón sin duplicar esta
// lógica — este script solo corre una vez en DOMContentLoaded, no ve tablas insertadas después.
(function () {
    function aplicarBotonExpandir(contenedor) {
        if (contenedor.dataset.expandibleListo) return;
        contenedor.dataset.expandibleListo = '1';

        var boton = document.createElement('button');
        boton.type = 'button';
        boton.className = 'btn-expandir-tabla';
        boton.textContent = 'Expandir tabla';

        boton.addEventListener('click', function () {
            var expandida = contenedor.classList.toggle('expandida');
            boton.textContent = expandida ? 'Compactar tabla' : 'Expandir tabla';
        });

        contenedor.insertAdjacentElement('beforebegin', boton);
    }

    document.addEventListener('DOMContentLoaded', function () {
        document.querySelectorAll('.tabla-vertical-limitada').forEach(aplicarBotonExpandir);
    });

    window.InsusegTablas = { aplicarBotonExpandir: aplicarBotonExpandir };
})();
