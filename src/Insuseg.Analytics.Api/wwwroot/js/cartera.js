document.addEventListener('DOMContentLoaded', function () {
    var tabla = document.getElementById('tabla-cartera');
    if (!tabla) return;

    var thead = tabla.querySelector('thead');
    var tbody = tabla.querySelector('tbody');
    var formateador = new Intl.NumberFormat('es-CL', { maximumFractionDigits: 0 });

    // Árbol de tres niveles (cliente → categoría → producto) en UNA sola tabla — categoría y producto
    // ya no son mini-tablas con su propio recuadro/scroll adentro de la fila (ver Insuseg.md, 2026-08-21:
    // esa estructura anidada era justo lo que hacía "incómodo maniobrar" al bajar de nivel). Ahora cada
    // categoría/producto es una fila más del MISMO <tbody>, insertada justo después de su padre, con
    // sangría — así solo hay un scroll (el de toda la tabla) en vez de uno adentro de otro.
    //
    // Por construcción, el "bloque" de un cliente es esa fila más TODOS los <tr> siguientes hasta el
    // próximo '.fila-cliente' (o el final de la tabla) — nunca se insertan filas hijas en otro lugar,
    // así que no hace falta ningún índice/mapa aparte para encontrarlas.
    var cacheCategorias = {};
    var cacheProductos = {};

    // --- Cuántas columnas tiene la tabla (para el colspan de "Cargando…"/vacío) y si el mes actual
    //     está desglosado en las 4 columnas Facturas/Guías/Total/% año ant. — leído del propio <thead>,
    //     no hardcodeado, así sigue funcionando si el desglose se prende/apaga según el filtro. ---
    var totalColumnas = thead.querySelectorAll('th').length;
    var desgloseActivo = !!thead.querySelector('th.col-mes-actual');

    function colspanFila() {
        return totalColumnas;
    }

    // --- Franja "estás viendo a…" — segunda fila del thead, pegajosa justo debajo de la de columnas.
    //     Solo aparece mientras el cliente sobre cuyo detalle se scrolleó sigue expandido; clic la
    //     colapsa sin tener que volver a subir hasta su fila (pedido del usuario, 2026-08-21). ---
    var filaClienteActual = document.createElement('tr');
    filaClienteActual.className = 'fila-cliente-actual-sticky';
    filaClienteActual.style.display = 'none';
    var tdClienteActual = document.createElement('td');
    var contenidoClienteActual = document.createElement('div');
    contenidoClienteActual.className = 'cliente-actual-contenido';
    var chevronClienteActual = document.createElement('span');
    chevronClienteActual.className = 'chevron';
    chevronClienteActual.textContent = '▶';
    var textoClienteActual = document.createElement('span');
    contenidoClienteActual.appendChild(chevronClienteActual);
    contenidoClienteActual.appendChild(textoClienteActual);
    tdClienteActual.appendChild(contenidoClienteActual);
    filaClienteActual.appendChild(tdClienteActual);
    thead.appendChild(filaClienteActual);

    var contenedorScroll = tabla.closest('.tabla-vertical-limitada');
    var filaClienteActualReferencia = null;

    function medirAlturaEncabezado() {
        var filaEncabezado = thead.querySelector('tr:not(.fila-cliente-actual-sticky)');
        tdClienteActual.style.top = filaEncabezado.getBoundingClientRect().height + 'px';
        tdClienteActual.colSpan = colspanFila();
    }

    filaClienteActual.addEventListener('click', function () {
        if (filaClienteActualReferencia) {
            alternarCliente(filaClienteActualReferencia);
        }
    });

    function actualizarBarraClienteActual() {
        if (!contenedorScroll) return;
        var rectContenedor = contenedorScroll.getBoundingClientRect();
        var limiteVisible = rectContenedor.top + thead.querySelector('tr:not(.fila-cliente-actual-sticky)').getBoundingClientRect().height;
        var clientes = tbody.querySelectorAll('tr.fila-cliente');
        var actual = null;
        for (var i = 0; i < clientes.length; i++) {
            var rectFila = clientes[i].getBoundingClientRect();
            if (rectFila.top <= limiteVisible + 1) {
                actual = clientes[i];
            } else {
                break; // las filas están en el mismo orden visual que el documento
            }
        }

        if (actual && actual.classList.contains('expandida')) {
            filaClienteActualReferencia = actual;
            textoClienteActual.replaceChildren(
                document.createTextNode('Viendo a '),
                (function () { var b = document.createElement('b'); b.textContent = actual.querySelector('.nombre-cliente-texto').textContent; return b; })(),
                document.createTextNode(' — clic para colapsar'));
            filaClienteActual.style.display = 'table-row';
        } else {
            filaClienteActualReferencia = null;
            filaClienteActual.style.display = 'none';
        }
    }

    var actualizacionPendiente = false;
    function pedirActualizacionBarra() {
        if (actualizacionPendiente) return;
        actualizacionPendiente = true;
        requestAnimationFrame(function () {
            actualizacionPendiente = false;
            actualizarBarraClienteActual();
        });
    }

    if (contenedorScroll) {
        medirAlturaEncabezado();
        contenedorScroll.addEventListener('scroll', pedirActualizacionBarra);
        window.addEventListener('resize', function () {
            medirAlturaEncabezado();
            pedirActualizacionBarra();
        });
    }

    // --- "Colapsar todo" — vuelve toda la tabla al estado inicial sin perder lo ya cargado (el caché
    //     de categorías/productos queda intacto, así que reabrir después no vuelve a pedir nada). ---
    var botonColapsarTodo = document.getElementById('btn-colapsar-todo');
    if (botonColapsarTodo) {
        botonColapsarTodo.addEventListener('click', function () {
            tbody.querySelectorAll('.expandida').forEach(function (fila) { fila.classList.remove('expandida'); });
            tbody.querySelectorAll('.fila-categoria, .fila-producto, .fila-estado').forEach(function (fila) {
                fila.style.display = 'none';
            });
            pedirActualizacionBarra();
        });
    }

    // --- Expandir/colapsar cliente (nivel 0) ---
    tabla.addEventListener('click', function (evento) {
        var filaCliente = evento.target.closest('.fila-cliente');
        if (filaCliente) {
            alternarCliente(filaCliente);
            return;
        }
        var filaCategoria = evento.target.closest('.fila-categoria');
        if (filaCategoria) {
            alternarCategoria(filaCategoria);
        }
    });

    function alternarCliente(filaCliente) {
        var cardCode = filaCliente.dataset.cardCode;
        var abierta = filaCliente.classList.toggle('expandida');

        if (abierta && !cacheCategorias[cardCode]) {
            var filaCarga = crearFilaCargando();
            filaCliente.after(filaCarga);
            cargarCategorias(cardCode, filaCliente, filaCarga);
            pedirActualizacionBarra();
            return;
        }
        sincronizarVisibilidadHijos(filaCliente);
        pedirActualizacionBarra();
    }

    function alternarCategoria(filaCategoria) {
        var cardCode = filaCategoria.dataset.cardCode;
        var categoriaCodigo = filaCategoria.dataset.categoriaCodigo;
        var clave = cardCode + '|' + categoriaCodigo;
        var abierta = filaCategoria.classList.toggle('expandida');

        if (abierta && !cacheProductos[clave]) {
            var filaCarga = crearFilaCargando();
            filaCategoria.after(filaCarga);
            cargarProductos(cardCode, categoriaCodigo, filaCategoria.dataset.categoriaNombre, filaCategoria, filaCarga);
            pedirActualizacionBarra();
            return;
        }
        sincronizarVisibilidadHijos(filaCategoria);
        pedirActualizacionBarra();
    }

    // Muestra/oculta los hijos DIRECTOS de una fila según si esta quedó expandida — los nietos (si los
    // hay) conservan su propio estado de expandida/colapsada de antes, esta función solo decide si se
    // VEN o no según la cadena de ancestros. Se llama también después de insertar filas nuevas.
    function sincronizarVisibilidadHijos(filaPadre) {
        var nivelPadre = nivelDe(filaPadre);
        var visiblePadre = filaPadre.style.display !== 'none';
        var mostrar = visiblePadre && filaPadre.classList.contains('expandida');
        var fila = filaPadre.nextElementSibling;
        while (fila && nivelDe(fila) > nivelPadre) {
            var esHijoDirecto = nivelDe(fila) === nivelPadre + 1;
            if (esHijoDirecto) {
                fila.style.display = mostrar ? '' : 'none';
                // Si el hijo directo tiene sus propios hijos y él mismo está expandido, hay que
                // sincronizarlos también (recursivo) — si el padre se está colapsando, sus hijos ya
                // quedan ocultos por el bucle igual, sin necesidad de tocar la clase 'expandida' de nadie.
                sincronizarVisibilidadHijos(fila);
            } else if (!mostrar) {
                fila.style.display = 'none';
            }
            fila = fila.nextElementSibling;
        }
    }

    function nivelDe(fila) {
        if (fila.classList.contains('fila-cliente')) return 0;
        if (fila.classList.contains('fila-categoria')) return 1;
        if (fila.classList.contains('fila-producto')) return 2;
        return 99; // filas de carga/vacío — cuentan como "más profundas que cualquier hermano real"
    }

    function crearFilaCargando() {
        var tr = document.createElement('tr');
        tr.className = 'fila-estado';
        var td = document.createElement('td');
        td.colSpan = colspanFila();
        td.className = 'fila-estado-texto';
        td.textContent = 'Cargando…';
        tr.appendChild(td);
        return tr;
    }

    function crearFilaMensaje(mensaje) {
        var tr = document.createElement('tr');
        tr.className = 'fila-estado';
        var td = document.createElement('td');
        td.colSpan = colspanFila();
        td.className = 'fila-estado-texto';
        td.textContent = mensaje;
        tr.appendChild(td);
        return tr;
    }

    // --- Carga de datos (AJAX perezoso, mismo patrón de siempre) ---

    function cargarCategorias(cardCode, filaCliente, filaCarga) {
        var params = new URLSearchParams(window.location.search);
        params.set('handler', 'Productos');
        params.set('cardCode', cardCode);

        fetch(window.location.pathname + '?' + params.toString())
            .then(function (respuesta) {
                if (!respuesta.ok) throw new Error('HTTP ' + respuesta.status);
                return respuesta.json();
            })
            .then(function (datos) {
                cacheCategorias[cardCode] = datos;
                var filas = datos.categorias.length === 0
                    ? [crearFilaMensaje('Sin categorías con ventas para este cliente en el período filtrado.')]
                    : datos.categorias.map(function (c) {
                        return crearFilaNivel(1, {
                            nombre: c.nombre,
                            montoPorMes: c.montoPorMes,
                            totalGeneral: c.totalGeneral,
                            promedioMes: c.promedioMes,
                            peso: c.pesoCategoria,
                            porcentajeCartera: c.porcentajeCartera,
                            porcentajeMargen: c.porcentajeMargen,
                        }, datos.meses, {
                            expandible: true,
                            etiquetaNivel: 'Categoría',
                            cardCode: cardCode,
                            categoriaCodigo: c.codigo,
                            categoriaNombre: c.nombre,
                        });
                    });
                filaCarga.replaceWith.apply(filaCarga, filas);
                // Por si el usuario colapsó el cliente mientras el fetch todavía estaba en camino —
                // las filas recién insertadas nacen visibles por defecto, esto las oculta si ya no
                // corresponde mostrarlas.
                sincronizarVisibilidadHijos(filaCliente);
                pedirActualizacionBarra();
            })
            .catch(function () {
                filaCarga.replaceWith(crearFilaMensaje('No se pudo cargar el detalle de categorías.'));
                pedirActualizacionBarra();
            });
    }

    function cargarProductos(cardCode, categoriaCodigo, categoriaNombre, filaCategoria, filaCarga) {
        var clave = cardCode + '|' + categoriaCodigo;
        var params = new URLSearchParams(window.location.search);
        params.set('handler', 'ProductosPorCategoria');
        params.set('cardCode', cardCode);
        params.set('categoriaCodigo', categoriaCodigo);

        fetch(window.location.pathname + '?' + params.toString())
            .then(function (respuesta) {
                if (!respuesta.ok) throw new Error('HTTP ' + respuesta.status);
                return respuesta.json();
            })
            .then(function (datos) {
                cacheProductos[clave] = datos;
                var filas = datos.productos.length === 0
                    ? [crearFilaMensaje('Sin líneas de detalle para esta categoría.')]
                    : datos.productos.map(function (p) {
                        return crearFilaNivel(2, {
                            nombre: p.nombre,
                            montoPorMes: p.montoPorMes,
                            totalGeneral: p.totalGeneral,
                            promedioMes: p.promedioMes,
                            peso: p.pesoProducto,
                            porcentajeCartera: p.porcentajeCartera,
                            porcentajeMargen: p.porcentajeMargen,
                        }, datos.meses, { expandible: false, etiquetaNivel: 'Producto' });
                    });
                filaCarga.replaceWith.apply(filaCarga, filas);
                sincronizarVisibilidadHijos(filaCategoria);
                pedirActualizacionBarra();
            })
            .catch(function () {
                filaCarga.replaceWith(crearFilaMensaje('No se pudo cargar el detalle de productos.'));
                pedirActualizacionBarra();
            });
    }

    // --- Construcción de una fila de nivel 1 (categoría) o 2 (producto) — mismas columnas/clases que
    //     la fila de cliente (nivel 0), para que todo alinee y el tinte de "mes actual" siga bajando por
    //     las columnas. Las 4 columnas de Facturas/Guías/Total/%año ant. no existen a este nivel de
    //     detalle (no se trackea guías pendientes por categoría/producto) — se muestran en blanco ("—")
    //     en vez de mostrar un número que se confundiría con "Total = Facturas + Guías" de la fila cliente. ---
    function crearFilaNivel(nivel, item, meses, opciones) {
        var tr = document.createElement('tr');
        tr.className = nivel === 1 ? 'fila-categoria' : 'fila-producto';
        if (opciones.expandible) {
            tr.dataset.cardCode = opciones.cardCode;
            tr.dataset.categoriaCodigo = opciones.categoriaCodigo;
            tr.dataset.categoriaNombre = opciones.categoriaNombre;
        }

        var tdN = document.createElement('td');
        tdN.className = 'col-sticky col-sticky-1';
        tr.appendChild(tdN);

        var tdNombre = document.createElement('td');
        tdNombre.className = 'col-sticky col-sticky-2';
        var envoltorio = document.createElement('div');
        envoltorio.className = 'nombre-cliente nombre-nivel' + nivel;
        var chevron = document.createElement('span');
        chevron.className = 'chevron' + (opciones.expandible ? '' : ' chevron-hoja');
        chevron.textContent = '▶';
        var texto = document.createElement('span');
        texto.className = 'nombre-cliente-texto';
        texto.textContent = item.nombre;
        var banda = document.createElement('span');
        banda.className = 'banda-nivel';
        banda.textContent = opciones.etiquetaNivel;
        envoltorio.appendChild(chevron);
        envoltorio.appendChild(texto);
        envoltorio.appendChild(banda);
        tdNombre.appendChild(envoltorio);
        tr.appendChild(tdNombre);

        // Meses "planos" — todos, salvo que el desglose del mes actual esté activo, en cuyo caso el
        // último mes de la lista se muestra aparte (las 4 columnas en blanco de más abajo).
        var mesesPlanos = desgloseActivo ? meses.slice(0, -1) : meses;
        mesesPlanos.forEach(function (etiquetaMes) {
            var monto = (item.montoPorMes && item.montoPorMes[etiquetaMes]) || 0;
            var td = document.createElement('td');
            td.textContent = monto === 0 ? '—' : formateador.format(monto);
            tr.appendChild(td);
        });

        if (desgloseActivo) {
            [
                'col-mes-actual col-mes-actual-inicio',
                'col-mes-actual',
                'col-mes-actual col-mes-actual-total',
                'col-mes-actual col-mes-actual-fin',
            ].forEach(function (clase) {
                var td = document.createElement('td');
                td.className = clase;
                td.textContent = '—';
                tr.appendChild(td);
            });
        }

        [
            ['col-total', formateador.format(item.totalGeneral)],
            ['col-calc', formateador.format(item.promedioMes)],
            ['col-calc', Math.round(item.peso) + '%'],
            ['col-calc', Math.round(item.porcentajeCartera) + '%'],
            ['col-mg', Math.round(item.porcentajeMargen) + '%'],
        ].forEach(function (par) {
            var td = document.createElement('td');
            td.className = par[0];
            td.textContent = par[1];
            tr.appendChild(td);
        });

        return tr;
    }

    // --- Buscador (por nombre de cliente) ---
    var buscador = document.getElementById('buscador-cartera');
    var contador = document.getElementById('cartera-contador');
    if (buscador) {
        buscador.addEventListener('input', function () {
            var termino = normalizarTexto(buscador.value);
            var visibles = 0;
            var total = 0;
            tabla.querySelectorAll('tbody > tr.fila-cliente').forEach(function (filaCliente) {
                total++;
                var nombre = normalizarTexto(filaCliente.querySelector('.nombre-cliente-texto').textContent);
                var coincide = termino === '' || nombre.indexOf(termino) !== -1;
                var bloque = obtenerBloqueCliente(filaCliente);
                if (coincide) {
                    visibles++;
                    filaCliente.style.display = '';
                    sincronizarVisibilidadHijos(filaCliente);
                } else {
                    // Si estaba expandida y deja de coincidir con la búsqueda, se cierra — si no, queda
                    // un detalle abierto "flotando" sin su fila visible.
                    filaCliente.classList.remove('expandida');
                    bloque.forEach(function (fila) { fila.style.display = 'none'; });
                }
            });
            contador.textContent = termino === '' ? '' : (visibles + ' de ' + total + ' cliente(s)');
            pedirActualizacionBarra();
        });
    }

    // Todas las filas de un cliente (la suya + todo lo insertado debajo, cualquier nivel) — por
    // construcción son exactamente los <tr> siguientes hasta el próximo '.fila-cliente'.
    function obtenerBloqueCliente(filaCliente) {
        var bloque = [filaCliente];
        var fila = filaCliente.nextElementSibling;
        while (fila && !fila.classList.contains('fila-cliente')) {
            bloque.push(fila);
            fila = fila.nextElementSibling;
        }
        return bloque;
    }

    // --- Encabezados ordenables (clic = ordena por esa columna; clic de nuevo = invierte el sentido) ---
    var estadoOrden = { indice: -1, direccion: 'desc' };

    thead.querySelectorAll('th.th-ordenable').forEach(function (th) {
        // El orden inicial real es "Total general" descendente (así viene ordenado desde el servidor,
        // ver CarteraModel) — se refleja acá para que la flecha ya salga puesta sin necesidad de un
        // clic, y para que el primer clic sobre esa misma columna la invierta en vez de "reiniciarla".
        if (th.classList.contains('orden-asc') || th.classList.contains('orden-desc')) {
            estadoOrden.indice = th.cellIndex;
            estadoOrden.direccion = th.classList.contains('orden-asc') ? 'asc' : 'desc';
        }

        th.addEventListener('click', function () {
            var nuevaDireccion = estadoOrden.indice === th.cellIndex && estadoOrden.direccion === 'asc' ? 'desc' : 'asc';
            ordenarTabla(th.cellIndex, th.dataset.sortTipo, nuevaDireccion);
            estadoOrden = { indice: th.cellIndex, direccion: nuevaDireccion };

            thead.querySelectorAll('th.th-ordenable').forEach(function (otro) {
                otro.classList.remove('orden-asc', 'orden-desc');
            });
            th.classList.add(nuevaDireccion === 'asc' ? 'orden-asc' : 'orden-desc');
        });
    });

    // Ordenar reordena los BLOQUES de cliente (fila + todo lo insertado debajo) como unidad — nunca
    // reordena categorías/productos entre sí, esta tabla solo ordena por cliente.
    function ordenarTabla(indiceColumna, tipo, direccion) {
        var factor = direccion === 'asc' ? 1 : -1;
        var bloques = [];
        tabla.querySelectorAll('tbody > tr.fila-cliente').forEach(function (filaCliente) {
            bloques.push({ filaCliente: filaCliente, filas: obtenerBloqueCliente(filaCliente) });
        });

        bloques.sort(function (a, b) {
            var va = valorDeCelda(a.filaCliente.cells[indiceColumna], tipo);
            var vb = valorDeCelda(b.filaCliente.cells[indiceColumna], tipo);
            var comparacion = tipo === 'numero' ? (va - vb) : va.localeCompare(vb, 'es');
            return comparacion * factor;
        });

        bloques.forEach(function (bloque) {
            bloque.filas.forEach(function (fila) { tbody.appendChild(fila); });
        });

        // El N° es la posición visual actual, no un id — se renumera después de cada orden nuevo.
        var n = 0;
        tbody.querySelectorAll('tr.fila-cliente').forEach(function (filaCliente) {
            n++;
            filaCliente.cells[0].textContent = n;
        });
        pedirActualizacionBarra();
    }

    function valorDeCelda(celda, tipo) {
        if (tipo === 'numero') {
            var valor = celda.dataset.valor;
            return valor === undefined ? 0 : parseFloat(valor);
        }
        var nombre = celda.querySelector('.nombre-cliente-texto');
        return (nombre ? nombre.textContent : celda.textContent).trim().toLowerCase();
    }

    function normalizarTexto(texto) {
        // NFD separa cada letra acentuada en (letra base + marca diacrítica); U+0300-U+036F es el
        // rango Unicode de esas marcas — sacándolas, "Núñez" y "Nunez" matchean igual en el buscador.
        return texto.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase().trim();
    }
});
