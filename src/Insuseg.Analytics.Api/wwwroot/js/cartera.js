document.addEventListener('DOMContentLoaded', function () {
    var tabla = document.getElementById('tabla-cartera');
    if (!tabla) return;

    // Dos niveles de detalle, cada uno con su propio caché: categorías por cliente, y productos por
    // (cliente, categoría). Todo se construye con el DOM (createElement/textContent), no strings HTML
    // concatenados — los nombres de categoría/producto son datos, no confían en no traer comillas.
    var cacheCategorias = {};
    var cacheProductos = {};
    var formateador = new Intl.NumberFormat('es-CL', { maximumFractionDigits: 0 });

    tabla.querySelectorAll('.fila-cliente').forEach(function (fila) {
        fila.addEventListener('click', function () {
            var cardCode = fila.dataset.cardCode;
            var filaDetalle = tabla.querySelector('[data-detalle-de="' + cardCode + '"]');
            var abierta = fila.classList.toggle('expandida');
            filaDetalle.classList.toggle('abierta', abierta);

            if (abierta && !cacheCategorias[cardCode]) {
                cargarCategorias(cardCode, fila, filaDetalle);
            }
        });
    });

    // --- Buscador (por nombre de cliente) ---
    var buscador = document.getElementById('buscador-cartera');
    var contador = document.getElementById('cartera-contador');
    if (buscador) {
        buscador.addEventListener('input', function () {
            var termino = normalizarTexto(buscador.value);
            var visibles = 0;
            var total = 0;
            tabla.querySelectorAll('tbody > tr.fila-cliente').forEach(function (fila) {
                total++;
                var nombre = normalizarTexto(fila.querySelector('.nombre-cliente-texto').textContent);
                var coincide = termino === '' || nombre.indexOf(termino) !== -1;
                fila.style.display = coincide ? '' : 'none';
                if (coincide) {
                    visibles++;
                } else {
                    // Si estaba expandida y deja de coincidir con la búsqueda, se cierra — si no, queda
                    // un detalle abierto "flotando" sin su fila visible.
                    fila.classList.remove('expandida');
                    fila.nextElementSibling.classList.remove('abierta');
                }
            });
            contador.textContent = termino === '' ? '' : (visibles + ' de ' + total + ' cliente(s)');
        });
    }

    // --- Encabezados ordenables (clic = ordena por esa columna; clic de nuevo = invierte el sentido) ---
    var thead = tabla.querySelector('thead');
    var tbody = tabla.querySelector('tbody');
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

    function ordenarTabla(indiceColumna, tipo, direccion) {
        var factor = direccion === 'asc' ? 1 : -1;
        var pares = [];
        tbody.querySelectorAll('tr.fila-cliente').forEach(function (fila) {
            pares.push({ fila: fila, detalle: fila.nextElementSibling });
        });

        pares.sort(function (a, b) {
            var va = valorDeCelda(a.fila.cells[indiceColumna], tipo);
            var vb = valorDeCelda(b.fila.cells[indiceColumna], tipo);
            var comparacion = tipo === 'numero' ? (va - vb) : va.localeCompare(vb, 'es');
            return comparacion * factor;
        });

        pares.forEach(function (par) {
            tbody.appendChild(par.fila);
            tbody.appendChild(par.detalle);
        });

        // El N° es la posición visual actual, no un id — se renumera después de cada orden nuevo.
        var n = 0;
        tbody.querySelectorAll('tr.fila-cliente').forEach(function (fila) {
            n++;
            fila.cells[0].textContent = n;
        });
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

    function cargarCategorias(cardCode, filaCliente, filaDetalle) {
        var celda = filaDetalle.querySelector('td');
        celda.replaceChildren(nota('Cargando…'));

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
                var nombreCliente = filaCliente.querySelector('.nombre-cliente-texto').textContent;
                celda.replaceChildren(renderDetalleCategorias(cardCode, nombreCliente, datos));
            })
            .catch(function () {
                celda.replaceChildren(nota('No se pudo cargar el detalle de categorías.'));
            });
    }

    function cargarProductos(cardCode, categoriaCodigo, categoriaNombre, celdaDetalleCategoria) {
        var clave = cardCode + '|' + categoriaCodigo;
        celdaDetalleCategoria.replaceChildren(nota('Cargando…'));

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
                celdaDetalleCategoria.replaceChildren(renderDetalleProductos(categoriaNombre, datos));
            })
            .catch(function () {
                celdaDetalleCategoria.replaceChildren(nota('No se pudo cargar el detalle de productos.'));
            });
    }

    // Con tablas anidadas dos niveles (cliente → categoría → producto), el truco CSS de "width:0 en
    // la celda del detalle" para evitar que la tabla externa se estire ya no alcanza — el contenido de
    // la mini-tabla (otra tabla adentro) tiene un ancho mínimo que igual la infla. Como refuerzo, se le
    // pone a cada mini-tabla un ancho máximo en píxeles medido contra un contenedor que sí es estable
    // (el ancho real de la página, no afectado por este problema), así el scroll propio de la mini-tabla
    // pasa a ser real en vez de heredar el scroll de toda la tabla de Cartera.
    function limitarAnchoContenedor(contenedor) {
        var referencia = document.querySelector('.insuseg-container');
        if (referencia) {
            contenedor.style.maxWidth = referencia.clientWidth + 'px';
        }
    }

    // --- Nivel 1: categorías ---

    function renderDetalleCategorias(cardCode, nombreCliente, datos) {
        var wrap = document.createElement('div');
        wrap.className = 'detalle-wrap';
        wrap.appendChild(encabezadoDetalle('Detalle por categoría — ' + nombreCliente));
        wrap.appendChild(nota(
            'Peso Categoría', ' = monto de la categoría ÷ total de este cliente. ',
            '% Cartera', ' = participación de este cliente dentro del total vendido de esa categoría a todos los clientes.'));

        var contenedorScroll = document.createElement('div');
        contenedorScroll.className = 'tabla-scroll tabla-vertical-limitada tabla-detalle-mini';
        limitarAnchoContenedor(contenedorScroll);
        var tabla = document.createElement('table');
        tabla.className = 'insuseg-table-mini';
        tabla.appendChild(construirEncabezado('Categoría', 'Peso Categoría', datos.meses));

        var tbody = document.createElement('tbody');
        if (datos.categorias.length === 0) {
            tbody.appendChild(filaVacia(datos.meses.length));
        } else {
            datos.categorias.forEach(function (c) {
                var celdas = [
                    c.nombre,
                    c.totalGeneral,
                    c.promedioMes,
                    Math.round(c.pesoCategoria) + '%',
                    Math.round(c.porcentajeCartera) + '%',
                    Math.round(c.porcentajeMargen) + '%',
                ];
                var filaDetalleCategoria = document.createElement('tr');
                filaDetalleCategoria.className = 'fila-detalle';
                var tdDetalleCategoria = document.createElement('td');
                tdDetalleCategoria.colSpan = datos.meses.length + 6;
                filaDetalleCategoria.appendChild(tdDetalleCategoria);

                var filaCategoria = construirFilaExpandible(c.nombre, c.montoPorMes, celdas, datos.meses, function (abierta) {
                    filaDetalleCategoria.classList.toggle('abierta', abierta);
                    if (abierta && !cacheProductos[cardCode + '|' + c.codigo]) {
                        cargarProductos(cardCode, c.codigo, c.nombre, tdDetalleCategoria);
                    }
                });

                tbody.appendChild(filaCategoria);
                tbody.appendChild(filaDetalleCategoria);
            });
        }
        tabla.appendChild(tbody);
        contenedorScroll.appendChild(tabla);
        wrap.appendChild(contenedorScroll);

        if (window.InsusegTablas) {
            window.InsusegTablas.aplicarBotonExpandir(contenedorScroll);
        }
        return wrap;
    }

    // --- Nivel 2: productos de una categoría ---

    function renderDetalleProductos(categoria, datos) {
        var wrap = document.createElement('div');
        wrap.className = 'detalle-wrap';
        wrap.appendChild(encabezadoDetalle('Detalle por producto — ' + categoria));
        wrap.appendChild(nota(
            'Peso Producto', ' = monto del producto ÷ total de esta categoría (para este cliente). ',
            '% Cartera', ' = participación de este cliente dentro del total vendido de ese producto a todos los clientes.'));

        var contenedorScroll = document.createElement('div');
        contenedorScroll.className = 'tabla-scroll tabla-vertical-limitada tabla-detalle-mini';
        limitarAnchoContenedor(contenedorScroll);
        var tabla = document.createElement('table');
        tabla.className = 'insuseg-table-mini';
        tabla.appendChild(construirEncabezado('Producto', 'Peso Producto', datos.meses));

        var tbody = document.createElement('tbody');
        if (datos.productos.length === 0) {
            tbody.appendChild(filaVacia(datos.meses.length));
        } else {
            datos.productos.forEach(function (p) {
                var celdas = [
                    p.nombre,
                    p.totalGeneral,
                    p.promedioMes,
                    Math.round(p.pesoProducto) + '%',
                    Math.round(p.porcentajeCartera) + '%',
                    Math.round(p.porcentajeMargen) + '%',
                ];
                tbody.appendChild(construirFilaSimple(p.montoPorMes, celdas, datos.meses));
            });
        }
        tabla.appendChild(tbody);
        contenedorScroll.appendChild(tabla);
        wrap.appendChild(contenedorScroll);

        if (window.InsusegTablas) {
            window.InsusegTablas.aplicarBotonExpandir(contenedorScroll);
        }
        return wrap;
    }

    // --- Helpers de construcción de DOM, compartidos por ambos niveles ---

    function encabezadoDetalle(texto) {
        var header = document.createElement('div');
        header.className = 'detalle-header';
        var h3 = document.createElement('h3');
        h3.textContent = texto;
        header.appendChild(h3);
        return header;
    }

    // Acepta pares (texto en negrita, texto normal) además de un mensaje simple de una sola cadena.
    function nota() {
        var p = document.createElement('p');
        p.className = 'detalle-nota';
        if (arguments.length === 1) {
            p.textContent = arguments[0];
            return p;
        }
        for (var i = 0; i < arguments.length; i += 2) {
            var strong = document.createElement('strong');
            strong.textContent = arguments[i];
            p.appendChild(strong);
            p.appendChild(document.createTextNode(arguments[i + 1]));
        }
        return p;
    }

    function construirEncabezado(colPrimera, colPeso, meses) {
        var thead = document.createElement('thead');
        var tr = document.createElement('tr');
        tr.appendChild(crearCelda('th', colPrimera, 'col-sticky col-sticky-solo'));
        meses.forEach(function (m) { tr.appendChild(crearCelda('th', m)); });
        ['Total general', 'Promedio Mes', colPeso, '% Cartera', '% MG'].forEach(function (texto) {
            tr.appendChild(crearCelda('th', texto));
        });
        thead.appendChild(tr);
        return thead;
    }

    // Fila de datos sin comportamiento de clic (hoja del árbol — un producto).
    function construirFilaSimple(montoPorMes, celdas, meses) {
        var tr = document.createElement('tr');
        tr.appendChild(crearCelda('td', celdas[0], 'col-sticky col-sticky-solo'));
        meses.forEach(function (m) {
            var monto = montoPorMes[m] || 0;
            tr.appendChild(crearCelda('td', monto === 0 ? '—' : formateador.format(monto)));
        });
        for (var i = 1; i < celdas.length; i++) {
            var valor = typeof celdas[i] === 'number' ? formateador.format(celdas[i]) : celdas[i];
            tr.appendChild(crearCelda('td', valor));
        }
        return tr;
    }

    // Fila de datos CON comportamiento de clic (nodo intermedio — una categoría, expande a productos).
    // Mismo look que .fila-cliente (chevron + nombre), reutilizando esas clases.
    function construirFilaExpandible(nombre, montoPorMes, celdas, meses, alCambiarAbierta) {
        var tr = construirFilaSimple(montoPorMes, celdas, meses);
        tr.classList.add('fila-categoria');

        var primeraCelda = tr.querySelector('td');
        var envoltorio = document.createElement('div');
        envoltorio.className = 'nombre-cliente';
        var chevron = document.createElement('span');
        chevron.className = 'chevron';
        chevron.textContent = '▶';
        var texto = document.createElement('span');
        texto.className = 'nombre-cliente-texto';
        texto.textContent = nombre;
        envoltorio.appendChild(chevron);
        envoltorio.appendChild(texto);
        primeraCelda.replaceChildren(envoltorio);

        tr.addEventListener('click', function () {
            var abierta = tr.classList.toggle('expandida');
            alCambiarAbierta(abierta);
        });

        return tr;
    }

    function filaVacia(cantidadMeses) {
        var tr = document.createElement('tr');
        var td = document.createElement('td');
        td.colSpan = cantidadMeses + 6;
        td.textContent = 'Sin líneas de detalle para este cliente.';
        tr.appendChild(td);
        return tr;
    }

    function crearCelda(tagName, contenido, claseCss) {
        var el = document.createElement(tagName);
        if (claseCss) el.className = claseCss;
        el.textContent = contenido;
        return el;
    }
});
