document.addEventListener('DOMContentLoaded', function () {
    var tabla = document.getElementById('tabla-cartera');
    if (!tabla) return;

    var cache = {};
    var formateador = new Intl.NumberFormat('es-CL', { maximumFractionDigits: 0 });

    tabla.querySelectorAll('.fila-cliente').forEach(function (fila) {
        fila.addEventListener('click', function () {
            var cardCode = fila.dataset.cardCode;
            var filaDetalle = tabla.querySelector('[data-detalle-de="' + cardCode + '"]');
            var abierta = fila.classList.toggle('expandida');
            filaDetalle.classList.toggle('abierta', abierta);

            if (abierta && !cache[cardCode]) {
                cargarDetalle(cardCode, fila, filaDetalle);
            }
        });
    });

    function cargarDetalle(cardCode, filaCliente, filaDetalle) {
        var celda = filaDetalle.querySelector('td');
        celda.innerHTML = '<div class="detalle-wrap"><p class="detalle-nota">Cargando…</p></div>';

        var params = new URLSearchParams(window.location.search);
        params.set('handler', 'Productos');
        params.set('cardCode', cardCode);

        fetch(window.location.pathname + '?' + params.toString())
            .then(function (respuesta) {
                if (!respuesta.ok) throw new Error('HTTP ' + respuesta.status);
                return respuesta.json();
            })
            .then(function (datos) {
                cache[cardCode] = datos;
                celda.innerHTML = renderDetalle(filaCliente, datos);
                var contenedorMini = celda.querySelector('.tabla-vertical-limitada');
                if (contenedorMini && window.InsusegTablas) {
                    window.InsusegTablas.aplicarBotonExpandir(contenedorMini);
                }
            })
            .catch(function () {
                celda.innerHTML = '<div class="detalle-wrap"><p class="detalle-nota">No se pudo cargar el detalle de productos.</p></div>';
            });
    }

    function renderDetalle(filaCliente, datos) {
        var nombreCliente = filaCliente.querySelector('.nombre-cliente-texto').textContent;
        var encabezadosMes = datos.meses.map(function (m) { return '<th>' + m + '</th>'; }).join('');

        var filasProducto = datos.productos.map(function (p) {
            var celdasMes = datos.meses.map(function (m) {
                var monto = p.montoPorMes[m] || 0;
                return '<td>' + (monto === 0 ? '—' : formateador.format(monto)) + '</td>';
            }).join('');

            return '<tr>' +
                '<td class="col-sticky col-sticky-solo">' + escapeHtml(p.nombre) + '</td>' +
                celdasMes +
                '<td>' + formateador.format(p.totalGeneral) + '</td>' +
                '<td>' + formateador.format(p.promedioMes) + '</td>' +
                '<td>' + Math.round(p.pesoProducto) + '%</td>' +
                '<td>' + Math.round(p.porcentajeCartera) + '%</td>' +
                '<td>' + Math.round(p.porcentajeMargen) + '%</td>' +
                '</tr>';
        }).join('');

        if (!filasProducto) {
            filasProducto = '<tr><td colspan="' + (datos.meses.length + 6) + '">Sin líneas de detalle para este cliente.</td></tr>';
        }

        return '<div class="detalle-wrap">' +
            '<div class="detalle-header"><h3>Detalle por producto — ' + escapeHtml(nombreCliente) + '</h3></div>' +
            '<p class="detalle-nota">' +
            '<strong>Peso Producto</strong> = monto del producto ÷ total del cliente. ' +
            '<strong>% Cartera</strong> = participación de este cliente dentro del total vendido de ese producto a todos los clientes.' +
            '</p>' +
            '<div class="tabla-scroll tabla-vertical-limitada tabla-detalle-mini"><table class="insuseg-table-mini"><thead><tr>' +
            '<th class="col-sticky col-sticky-solo">Producto</th>' + encabezadosMes +
            '<th>Total general</th><th>Promedio Mes</th><th>Peso Producto</th><th>% Cartera</th><th>% MG</th>' +
            '</tr></thead><tbody>' + filasProducto + '</tbody></table></div>' +
            '</div>';
    }

    function escapeHtml(texto) {
        var div = document.createElement('div');
        div.textContent = texto;
        return div.innerHTML;
    }
});
