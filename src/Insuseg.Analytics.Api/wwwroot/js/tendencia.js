document.addEventListener('DOMContentLoaded', function () {
    document.querySelectorAll('.viz-tendencia').forEach(function (contenedor) {
        var puntos;
        try {
            puntos = JSON.parse(contenedor.dataset.puntos || '[]');
        } catch (e) {
            return;
        }
        if (puntos.length === 0) return;

        var svg = contenedor.querySelector('.viz-svg');
        var crosshairLinea = contenedor.querySelector('.viz-crosshair');
        var crosshairPunto = contenedor.querySelector('.viz-crosshair-punto');
        var tooltip = contenedor.querySelector('.viz-tooltip');
        var formateador = new Intl.NumberFormat('es-CL', { maximumFractionDigits: 0 });

        function textoComparacion(punto) {
            if (punto.estado === 'SinDatos' || punto.diferencia === null || punto.diferencia === undefined) {
                return '○ Sin datos del mismo mes del año anterior para comparar';
            }
            if (punto.diferencia <= 0) {
                return '● Superó el mismo mes del año anterior por $' + formateador.format(Math.abs(punto.diferencia));
            }
            return '◆ No alcanzó el mismo mes del año anterior (faltan $' + formateador.format(punto.diferencia) + ')';
        }

        function puntoMasCercano(xSvg) {
            var mejor = puntos[0];
            var mejorDistancia = Math.abs(puntos[0].x - xSvg);
            for (var i = 1; i < puntos.length; i++) {
                var distancia = Math.abs(puntos[i].x - xSvg);
                if (distancia < mejorDistancia) {
                    mejor = puntos[i];
                    mejorDistancia = distancia;
                }
            }
            return mejor;
        }

        function mostrar(evento) {
            var caja = svg.getBoundingClientRect();
            var viewBox = svg.viewBox.baseVal;
            var xSvg = (evento.clientX - caja.left) / caja.width * viewBox.width;
            var punto = puntoMasCercano(xSvg);

            crosshairLinea.setAttribute('x1', punto.x);
            crosshairLinea.setAttribute('x2', punto.x);
            crosshairLinea.style.display = '';
            crosshairPunto.setAttribute('cx', punto.x);
            crosshairPunto.setAttribute('cy', punto.y);
            crosshairPunto.style.display = '';

            tooltip.textContent = '';
            var valor = document.createElement('strong');
            valor.textContent = '$' + formateador.format(punto.monto);
            var etiqueta = document.createElement('span');
            etiqueta.textContent = punto.etiqueta;
            var comparacion = document.createElement('span');
            comparacion.textContent = textoComparacion(punto);
            tooltip.appendChild(valor);
            tooltip.appendChild(etiqueta);
            tooltip.appendChild(comparacion);

            var xPct = punto.x / viewBox.width * 100;
            tooltip.style.left = xPct + '%';
            tooltip.style.display = '';
            tooltip.classList.toggle('viz-tooltip-derecha', xPct > 70);
        }

        function ocultar() {
            crosshairLinea.style.display = 'none';
            crosshairPunto.style.display = 'none';
            tooltip.style.display = 'none';
        }

        svg.addEventListener('pointermove', mostrar);
        svg.addEventListener('pointerleave', ocultar);
    });
});
