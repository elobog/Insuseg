using Insuseg.Analytics.Data;
using Insuseg.Analytics.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Insuseg.Analytics.Api.Pages.Ventas;

[Authorize(AuthenticationSchemes = "Identity.Application")]
public class CarteraModel : PageModel
{
    private static readonly string[] AbreviaturasMes =
        ["ene", "feb", "mar", "abr", "may", "jun", "jul", "ago", "sep", "oct", "nov", "dic"];

    // Código que no existe en SAP (los códigos reales son positivos) — se usa como VendedorCodigo de
    // un Vendedor sin asignar todavía, para que todos los filtros existentes (que ya hacían
    // `x.SalesPersonCode == VendedorCodigo`) devuelvan cero filas sin necesitar lógica especial en
    // cada consulta. Ver AplicarRestriccionVendedorAsync.
    private const int CodigoVendedorSinAsignar = -1;

    private readonly InsusegAnalyticsDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;

    public CarteraModel(InsusegAnalyticsDbContext db, UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _userManager = userManager;
    }

    [BindProperty(SupportsGet = true)]
    public DateOnly? Desde { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? Hasta { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? VendedorCodigo { get; set; }

    // Un usuario con rol Vendedor no elige VendedorCodigo — se le fuerza al suyo (ver
    // AplicarRestriccionVendedorAsync), sin importar qué haya llegado en la URL. La vista usa estas
    // dos propiedades para ocultar el filtro de vendedor y avisar si todavía no tiene uno asignado.
    public bool EsVendedorRestringido { get; private set; }
    public bool VendedorSinAsignar { get; private set; }

    public List<MesClave> Meses { get; private set; } = [];
    public MesClave? MesActual { get; private set; }

    // Ritmo esperado del mes en curso (0-100), para pintar en verde/rojo la columna "% año ant.":
    // un cliente que ya lleva más de este % del año pasado va "adelantado" al punto del mes en el que
    // estamos, aunque el mes todavía no haya cerrado. Solo tiene sentido cuando DesgloseMesActualDisponible
    // es true — ver esa propiedad.
    public decimal RitmoEsperadoMesActualPct { get; private set; }

    // El desglose de 4 columnas (Facturas/Guías/Total/% vs año anterior) solo tiene sentido cuando el
    // último mes del rango filtrado es el mes calendario REAL de hoy — no simplemente "el último mes
    // del filtro". La tabla DeliveryNotes (guías) es una foto de "lo que está abierto AHORA MISMO", no
    // un historial (reemplazo completo en cada sync, ver DeliveryNoteSyncService): si alguien filtra
    // Hasta a una fecha pasada, las guías de ese mes que ya se facturaron hace rato simplemente ya no
    // están en la tabla, así que "Guías" mostraría un número artificialmente bajo/incompleto, no la
    // foto real de lo que hubo ese mes en su momento. Por eso el desglose se apaga fuera del mes en
    // curso real y ese mes se muestra con el monto simple, igual que cualquier otro mes cerrado — ver
    // Insuseg.md, 2026-08-12.
    public bool DesgloseMesActualDisponible { get; private set; }
    public List<VendedorOpcion> Vendedores { get; private set; } = [];
    public List<ClienteCartera> Clientes { get; private set; } = [];

    // Total de la columna "Guías" (encabezado de la tabla, ver Cartera.cshtml) — se calcula del
    // diccionario completo guiasPorCliente, NO de Model.Clientes.Sum(c => c.GuiasMesActual): Clientes
    // sale de agrupar SaleLines (ventas facturadas), así que un cliente con guías pendientes pero CERO
    // facturas en todo el período filtrado no aparecería ahí, y el total se quedaría corto respecto al
    // que se puede verificar contra SAP.
    public decimal GuiasMesActualTotal { get; private set; }

    // Mismo patrón de "total en el encabezado" que GuiasMesActualTotal, para las otras dos columnas
    // del grupo del mes actual. FacturasMesActualTotal es literalmente VentaMesActual (ya calculado
    // más abajo) — se expone con este nombre para que la vista no tenga que saber que son lo mismo.
    // TotalMesActualTotal = Facturas + Guías, mismo criterio que la columna "Total" por fila.
    public decimal FacturasMesActualTotal => VentaMesActual;
    public decimal TotalMesActualTotal => VentaMesActual + GuiasMesActualTotal;

    public decimal VentaTotalPeriodo { get; private set; }
    public decimal VentaPromedioMes { get; private set; }
    public decimal VentaMesActual { get; private set; }
    public decimal MargenMesActual { get; private set; }
    public decimal PorcentajeMargenMesActual { get; private set; }
    public decimal VentaMesAnioAnterior { get; private set; }
    public string? MesAnioAnteriorEtiqueta { get; private set; }
    public decimal MargenTotalPeriodo { get; private set; }
    public decimal PorcentajeMargenPeriodo { get; private set; }
    public decimal DiferenciaAnioAnterior { get; private set; }
    public List<PuntoTendencia> TendenciaMensual { get; private set; } = [];
    public List<PuntoMargenMensual> MargenMensual { get; private set; } = [];

    // Fuerza VendedorCodigo al vendedor asignado del usuario actual si su rol es Vendedor — se llama
    // al principio de CADA handler GET de esta página (no solo OnGetAsync), porque el detalle por
    // categoría/producto (OnGetProductosAsync/OnGetProductosPorCategoriaAsync) recibe su propio
    // VendedorCodigo por querystring y podría, si no se repite acá, dejar ver el detalle de un
    // cliente ajeno con solo cambiar el cardCode en la URL. Esto es la restricción real (server-side);
    // ocultar el filtro en la vista es solo para que no confunda, no es la protección en sí.
    private async Task AplicarRestriccionVendedorAsync()
    {
        if (!User.IsInRole("Vendedor"))
        {
            return;
        }

        EsVendedorRestringido = true;
        var usuario = await _userManager.GetUserAsync(User);
        if (usuario?.SalesPersonCode is int codigo)
        {
            VendedorCodigo = codigo;
        }
        else
        {
            VendedorSinAsignar = true;
            VendedorCodigo = CodigoVendedorSinAsignar;
        }
    }

    public async Task OnGetAsync(CancellationToken ct)
    {
        AplicarPeriodoPorDefecto();
        await AplicarRestriccionVendedorAsync();
        Meses = GenerarMeses(Desde!.Value, Hasta!.Value);
        MesActual = Meses.Count > 0 ? Meses[^1] : null;

        var hoy = DateOnly.FromDateTime(DateTime.UtcNow);
        DesgloseMesActualDisponible = MesActual is not null && MesActual.Anio == hoy.Year && MesActual.Mes == hoy.Month;
        if (DesgloseMesActualDisponible)
        {
            var diasDelMesDeHasta = DateTime.DaysInMonth(Hasta.Value.Year, Hasta.Value.Month);
            RitmoEsperadoMesActualPct = (decimal)Hasta.Value.Day / diasDelMesDeHasta * 100m;
        }

        var vendedorNombres = await _db.SalesPersons
            .ToDictionaryAsync(sp => sp.SalesEmployeeCode, sp => sp.SalesEmployeeName, ct);

        // Todo se calcula desde las líneas (SaleLine.LineTotal), no desde Sale.Amount: Amount es el
        // DocTotal de SAP, que en Chile incluye IVA, mientras que LineTotal es el neto por línea (sin
        // IVA, estándar SAP B1). Un mismo query de líneas alimenta montos y margen, para que ambos
        // queden en la misma base (neta).
        var lineasPeriodo = _db.SaleLines
            .Join(_db.Sales,
                l => new { l.DocEntry, l.SourceDocType },
                s => new { s.DocEntry, s.SourceDocType },
                (l, s) => new { s.CardCode, s.CardName, s.SaleDate, s.SalesPersonCode, l.LineTotal, l.GrossBuyPrice, l.Quantity })
            .Where(x => x.SaleDate >= Desde && x.SaleDate <= Hasta);

        // Total neto por vendedor sobre TODO el período (nunca filtrado por VendedorCodigo) — % Cartera
        // de cada cliente es contra el total neto de SU vendedor asignado, no del vendedor que esté
        // seleccionado en el filtro de arriba.
        var totalPorVendedor = await lineasPeriodo
            .Where(x => x.SalesPersonCode != null)
            .GroupBy(x => x.SalesPersonCode!.Value)
            .Select(g => new { Codigo = g.Key, Total = g.Sum(x => x.LineTotal) })
            .ToDictionaryAsync(x => x.Codigo, x => x.Total, ct);

        // Un Vendedor no elige de esta lista (el filtro va oculto en la vista, ver EsVendedorRestringido)
        // — se deja vacía a propósito para no exponer en el modelo de la página los nombres/códigos de
        // otros vendedores, aunque la vista no los vaya a renderizar.
        Vendedores = EsVendedorRestringido
            ? []
            : totalPorVendedor.Keys
                .Select(codigo => new VendedorOpcion(codigo, NombreVendedor(codigo, vendedorNombres)))
                .OrderBy(v => v.Nombre)
                .ToList();

        var consulta = lineasPeriodo;
        if (VendedorCodigo.HasValue)
        {
            consulta = consulta.Where(x => x.SalesPersonCode == VendedorCodigo);
        }

        var lineasCrudo = await consulta.ToListAsync(ct);

        VentaTotalPeriodo = lineasCrudo.Sum(v => v.LineTotal);
        var lineasMesActual = MesActual is null
            ? []
            : lineasCrudo.Where(v => v.SaleDate.Year == MesActual.Anio && v.SaleDate.Month == MesActual.Mes);
        VentaMesActual = lineasMesActual.Sum(v => v.LineTotal);
        MargenMesActual = lineasMesActual.Sum(v => v.LineTotal - v.GrossBuyPrice * v.Quantity);
        PorcentajeMargenMesActual = VentaMesActual > 0 ? MargenMesActual / VentaMesActual * 100m : 0m;

        // El mes en curso casi nunca está cerrado, así que se excluye del promedio — comparar contra
        // un promedio "inflado" por meses completos haría que el mes actual siempre pareciera bajo.
        var mesesCompletos = Meses.Count > 1 ? Meses.Count - 1 : 1;
        var ventaMesesCompletos = Meses.Count > 1 ? VentaTotalPeriodo - VentaMesActual : VentaTotalPeriodo;
        VentaPromedioMes = ventaMesesCompletos / mesesCompletos;

        // "Faltan / superado" se compara contra el mismo mes del año anterior, no contra el promedio
        // del período — el mismo mes del año pasado casi siempre cae fuera del rango filtrado (por
        // defecto son solo 12 meses), así que se consulta aparte, con el mismo filtro de vendedor.
        // Se agrupa también por cliente (no solo el total agregado) — lo usa la columna "% vs año
        // anterior" del mes actual en la tabla, una sola consulta sirve para ambas cosas.
        var ventaMesAnioAnteriorPorCliente = new Dictionary<string, decimal>();
        if (MesActual is not null)
        {
            var inicioAnioAnterior = new DateOnly(MesActual.Anio - 1, MesActual.Mes, 1);
            var finAnioAnterior = inicioAnioAnterior.AddMonths(1).AddDays(-1);
            MesAnioAnteriorEtiqueta =
                $"{AbreviaturasMes[inicioAnioAnterior.Month - 1]}-{inicioAnioAnterior.Year % 100:D2}";

            var queryAnioAnterior = _db.SaleLines
                .Join(_db.Sales,
                    l => new { l.DocEntry, l.SourceDocType },
                    s => new { s.DocEntry, s.SourceDocType },
                    (l, s) => new { s.CardCode, s.SaleDate, s.SalesPersonCode, l.LineTotal })
                .Where(x => x.SaleDate >= inicioAnioAnterior && x.SaleDate <= finAnioAnterior);
            if (VendedorCodigo.HasValue)
            {
                queryAnioAnterior = queryAnioAnterior.Where(x => x.SalesPersonCode == VendedorCodigo);
            }

            var lineasAnioAnterior = await queryAnioAnterior.ToListAsync(ct);
            VentaMesAnioAnterior = lineasAnioAnterior.Sum(x => x.LineTotal);
            ventaMesAnioAnteriorPorCliente = lineasAnioAnterior
                .GroupBy(x => x.CardCode)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.LineTotal));
        }

        DiferenciaAnioAnterior = VentaMesAnioAnterior - VentaMesActual;

        // "Guías" — TODAS las guías de despacho pendientes de facturar de cada cliente, sin filtro de
        // fecha (decisión del usuario, 2026-08-17: antes se acotaba a las guías con DocDate dentro del
        // mes actual, lo que dejaba afuera cualquier guía pendiente de un mes anterior — confirmado
        // contra datos reales que había guías de hasta 2025-05 invisibles en Cartera por ese filtro).
        // Sigue gateado por DesgloseMesActualDisponible porque "Guías" es una de las 4 columnas del
        // grupo del mes en curso (Facturas/Guías/Total/% año ant.) — Facturas sí sigue acotada al mes
        // actual, así que el grupo entero solo tiene sentido cuando el filtro de fechas llega hasta el
        // mes calendario real de hoy (ver esa propiedad). DeliveryNoteLines ya viene pre-filtrada por
        // DeliveryNoteSyncService a solo líneas que pasan las tres reglas del modelo (LineStatus,
        // muestra/no-venta, piso de monto) — acá no hace falta repetir nada de eso, solo agrupar. El
        // filtro de vendedor usa l.SalesPersonCode (de la LÍNEA, no de la cabecera de la guía) porque
        // pueden diferir en SAP — mismo criterio que usa DeliveryNoteSyncService para persistir.
        var guiasPorCliente = new Dictionary<string, decimal>();
        if (DesgloseMesActualDisponible)
        {
            var queryGuias = _db.DeliveryNoteLines
                .Join(_db.DeliveryNotes,
                    l => l.DocEntry,
                    d => d.DocEntry,
                    (l, d) => new { d.CardCode, l.SalesPersonCode, l.LineTotal });
            if (VendedorCodigo.HasValue)
            {
                queryGuias = queryGuias.Where(x => x.SalesPersonCode == VendedorCodigo);
            }

            guiasPorCliente = (await queryGuias.ToListAsync(ct))
                .GroupBy(x => x.CardCode)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.LineTotal));
        }

        GuiasMesActualTotal = guiasPorCliente.Values.Sum();

        MargenTotalPeriodo = lineasCrudo.Sum(v => v.LineTotal - v.GrossBuyPrice * v.Quantity);
        PorcentajeMargenPeriodo = VentaTotalPeriodo > 0 ? MargenTotalPeriodo / VentaTotalPeriodo * 100m : 0m;

        // Comparación mes a mes contra el mismo mes del año anterior, para los puntos de estado que
        // se dibujan en ambos gráficos (verde = superó, naranja oscuro = no alcanzó, gris = sin datos
        // porque ese mes cae antes del horizonte histórico realmente cargado). El horizonte se resuelve
        // dinámicamente (MIN(SaleDate) real en la base) en vez de hardcodear 2024-01-01 de nuevo, así
        // sigue correcto si el horizonte del proyecto (sección 2 de Insuseg.md) cambia más adelante.
        var comparacionPorMes = new Dictionary<(int Anio, int Mes), (decimal Monto, decimal Margen)>();
        var horizonteDatos = DateOnly.MaxValue;
        if (Meses.Count > 0)
        {
            horizonteDatos = await _db.Sales.MinAsync(s => (DateOnly?)s.SaleDate, ct) ?? DateOnly.MaxValue;

            var inicioComparacion = new DateOnly(Meses[0].Anio - 1, Meses[0].Mes, 1);
            var finComparacion = new DateOnly(Meses[^1].Anio - 1, Meses[^1].Mes, 1).AddMonths(1).AddDays(-1);

            var queryComparacion = _db.SaleLines
                .Join(_db.Sales,
                    l => new { l.DocEntry, l.SourceDocType },
                    s => new { s.DocEntry, s.SourceDocType },
                    (l, s) => new { s.SaleDate, s.SalesPersonCode, l.LineTotal, l.GrossBuyPrice, l.Quantity })
                .Where(x => x.SaleDate >= inicioComparacion && x.SaleDate <= finComparacion);
            if (VendedorCodigo.HasValue)
            {
                queryComparacion = queryComparacion.Where(x => x.SalesPersonCode == VendedorCodigo);
            }

            // Se materializa primero (ToListAsync) y se agrupa después en memoria — mismo patrón que
            // ya usa el resto de esta página para evitar los problemas de traducción de EF Core con
            // GroupBy (ver nota del proyecto sobre este tema).
            comparacionPorMes = (await queryComparacion.ToListAsync(ct))
                .GroupBy(x => (x.SaleDate.Year, x.SaleDate.Month))
                .ToDictionary(g => g.Key, g => (
                    Monto: g.Sum(x => x.LineTotal),
                    Margen: g.Sum(x => x.LineTotal - x.GrossBuyPrice * x.Quantity)));
        }

        // Diferencia = año anterior − actual, mismo signo que ya usa DiferenciaAnioAnterior arriba
        // (positivo = no alcanzó / "faltan", negativo o cero = superó). null = sin datos de comparación.
        (decimal? Diferencia, EstadoComparacionMes Estado) ComparaConAnioAnterior(MesClave m, decimal valorActual, bool margen)
        {
            var finMesAnioAnterior = new DateOnly(m.Anio - 1, m.Mes, 1).AddMonths(1).AddDays(-1);
            if (finMesAnioAnterior < horizonteDatos)
            {
                return (null, EstadoComparacionMes.SinDatos);
            }

            var valorAnioAnterior = comparacionPorMes.TryGetValue((m.Anio - 1, m.Mes), out var v)
                ? (margen ? v.Margen : v.Monto)
                : 0m;
            var diferencia = valorAnioAnterior - valorActual;
            return (diferencia, diferencia > 0 ? EstadoComparacionMes.NoAlcanzado : EstadoComparacionMes.Superado);
        }

        TendenciaMensual = Meses
            .Select(m =>
            {
                var montoMes = lineasCrudo.Where(v => v.SaleDate.Year == m.Anio && v.SaleDate.Month == m.Mes).Sum(v => v.LineTotal);
                var (diferencia, estado) = ComparaConAnioAnterior(m, montoMes, margen: false);
                return new PuntoTendencia(m.Etiqueta, montoMes, estado, diferencia);
            })
            .ToList();

        MargenMensual = Meses
            .Select(m =>
            {
                var lineasMes = lineasCrudo.Where(v => v.SaleDate.Year == m.Anio && v.SaleDate.Month == m.Mes).ToList();
                var ventaMes = lineasMes.Sum(v => v.LineTotal);
                var margenMes = lineasMes.Sum(v => v.LineTotal - v.GrossBuyPrice * v.Quantity);
                var (diferencia, estado) = ComparaConAnioAnterior(m, margenMes, margen: true);
                return new PuntoMargenMensual(
                    m.Etiqueta,
                    margenMes,
                    ventaMes > 0 ? margenMes / ventaMes * 100m : 0m,
                    estado,
                    diferencia);
            })
            .ToList();

        Clientes = lineasCrudo
            .GroupBy(v => new { v.CardCode, v.CardName })
            .Select(g =>
            {
                var montoPorMes = Meses.ToDictionary(
                    m => (m.Anio, m.Mes),
                    m => g.Where(v => v.SaleDate.Year == m.Anio && v.SaleDate.Month == m.Mes).Sum(v => v.LineTotal));

                var total = g.Sum(v => v.LineTotal);
                var vendedorCodigo = g.Select(v => v.SalesPersonCode).FirstOrDefault(c => c.HasValue);
                var totalVendedor = vendedorCodigo.HasValue
                    ? totalPorVendedor.GetValueOrDefault(vendedorCodigo.Value)
                    : 0m;
                var costo = g.Sum(v => v.GrossBuyPrice * v.Quantity);
                var margen = total - costo;

                // Desglose del mes actual (Facturas/Guías/Total/% vs año anterior) — Facturas sigue
                // acotada al mes en curso, pero Guías ahora es el total pendiente de facturar de TODO
                // el historial (ver guiasPorCliente arriba) — decisión del usuario, 2026-08-17.
                var facturasMesActual = MesActual is not null
                    ? montoPorMes.GetValueOrDefault((MesActual.Anio, MesActual.Mes))
                    : 0m;
                var guiasMesActual = guiasPorCliente.GetValueOrDefault(g.Key.CardCode);
                var totalMesActual = facturasMesActual + guiasMesActual;
                var ventaAnioAnteriorCliente = ventaMesAnioAnteriorPorCliente.GetValueOrDefault(g.Key.CardCode);
                var porcentajeVsAnioAnterior = ventaAnioAnteriorCliente > 0
                    ? totalMesActual / ventaAnioAnteriorCliente * 100m
                    : (decimal?)null;

                return new ClienteCartera(
                    g.Key.CardCode,
                    g.Key.CardName,
                    NombreVendedor(vendedorCodigo, vendedorNombres),
                    montoPorMes,
                    total,
                    Meses.Count > 0 ? total / Meses.Count : 0m,
                    VentaTotalPeriodo > 0 ? total / VentaTotalPeriodo * 100m : 0m,
                    totalVendedor > 0 ? total / totalVendedor * 100m : 0m,
                    total > 0 ? margen / total * 100m : 0m,
                    facturasMesActual,
                    guiasMesActual,
                    totalMesActual,
                    porcentajeVsAnioAnterior);
            })
            .OrderByDescending(c => c.TotalGeneral)
            .ToList();
    }

    // Se usa cuando un producto no tiene U_Categoria cargado en SAP (catálogo viejo/incompleto), o el
    // código no matchea ninguna fila de ItemCategories — para que igual aparezca agrupado en algún
    // lado en vez de desaparecer del detalle. "" (no null) es la clave interna de agrupación/URL para
    // este caso, porque los códigos reales de SAP son numéricos como string ("1", "4"...).
    private const string SinCategoriaCodigo = "";
    private const string SinCategoriaNombre = "(Sin categoría)";

    // Primer nivel del detalle de un cliente en Cartera: categorías de producto, no productos — se
    // hace clic en una categoría para ver sus productos (OnGetProductosPorCategoriaAsync). Antes este
    // handler devolvía productos directo; ese comportamiento se movió al nuevo handler.
    //
    // Items.CategoryCode (U_Categoria en SAP) es un CÓDIGO, no el nombre — el nombre real vive en
    // ItemCategories (tabla U_ZCAT de SAP, ver BDhana.md). Por eso se resuelve acá y se manda al
    // cliente tanto el código (clave estable para el drill-down) como el nombre (solo para mostrar).
    public async Task<IActionResult> OnGetProductosAsync(string cardCode, CancellationToken ct)
    {
        AplicarPeriodoPorDefecto();
        await AplicarRestriccionVendedorAsync();
        var meses = GenerarMeses(Desde!.Value, Hasta!.Value);
        var categoriaCodigoPorItem = await _db.Items.ToDictionaryAsync(i => i.ItemCode, i => i.CategoryCode, ct);
        var nombrePorCategoriaCodigo = await _db.ItemCategories.ToDictionaryAsync(c => c.Code, c => c.Name, ct);
        string NombreDeCategoria(string codigo) =>
            codigo == SinCategoriaCodigo ? SinCategoriaNombre : nombrePorCategoriaCodigo.GetValueOrDefault(codigo, SinCategoriaNombre);

        // Total vendido de cada categoría a TODOS los clientes en el período — siempre sin el filtro de
        // vendedor, mismo criterio que el resto de los "% Cartera" de esta página: la base de
        // comparación es una propiedad de la categoría, no del filtro que esté activo.
        var lineasPeriodoGlobal = await _db.SaleLines
            .Join(_db.Sales,
                l => new { l.DocEntry, l.SourceDocType },
                s => new { s.DocEntry, s.SourceDocType },
                (l, s) => new { s.SaleDate, l.ItemCode, l.LineTotal })
            .Where(x => x.SaleDate >= Desde && x.SaleDate <= Hasta)
            .ToListAsync(ct);
        var totalPorCategoriaGlobal = lineasPeriodoGlobal
            .GroupBy(x => categoriaCodigoPorItem.GetValueOrDefault(x.ItemCode) ?? SinCategoriaCodigo)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.LineTotal));

        var lineasClienteQuery = _db.SaleLines
            .Join(_db.Sales,
                l => new { l.DocEntry, l.SourceDocType },
                s => new { s.DocEntry, s.SourceDocType },
                (l, s) => new { s.CardCode, s.SaleDate, s.SalesPersonCode, l.ItemCode, l.Quantity, l.LineTotal, l.GrossBuyPrice })
            .Where(x => x.SaleDate >= Desde && x.SaleDate <= Hasta && x.CardCode == cardCode);

        if (VendedorCodigo.HasValue)
        {
            lineasClienteQuery = lineasClienteQuery.Where(x => x.SalesPersonCode == VendedorCodigo);
        }

        var lineasCliente = await lineasClienteQuery
            .Select(x => new { x.SaleDate, x.ItemCode, x.Quantity, x.LineTotal, x.GrossBuyPrice })
            .ToListAsync(ct);

        var totalCliente = lineasCliente.Sum(x => x.LineTotal);

        var categorias = lineasCliente
            .GroupBy(x => categoriaCodigoPorItem.GetValueOrDefault(x.ItemCode) ?? SinCategoriaCodigo)
            .Select(g =>
            {
                var montoPorMes = meses.ToDictionary(
                    m => m.Etiqueta,
                    m => g.Where(x => x.SaleDate.Year == m.Anio && x.SaleDate.Month == m.Mes).Sum(x => x.LineTotal));

                var vendido = g.Sum(x => x.LineTotal);
                var costo = g.Sum(x => x.GrossBuyPrice * x.Quantity);
                var margen = vendido - costo;
                var totalGlobalCategoria = totalPorCategoriaGlobal.GetValueOrDefault(g.Key);

                return new
                {
                    codigo = g.Key,
                    nombre = NombreDeCategoria(g.Key),
                    montoPorMes,
                    totalGeneral = vendido,
                    promedioMes = meses.Count > 0 ? vendido / meses.Count : 0m,
                    pesoCategoria = totalCliente > 0 ? vendido / totalCliente * 100m : 0m,
                    porcentajeCartera = totalGlobalCategoria > 0 ? vendido / totalGlobalCategoria * 100m : 0m,
                    porcentajeMargen = vendido > 0 ? margen / vendido * 100m : 0m,
                };
            })
            .OrderByDescending(c => c.totalGeneral)
            .ToList();

        return new JsonResult(new { meses = meses.Select(m => m.Etiqueta), categorias });
    }

    // Segundo nivel: productos de una categoría puntual (por código) para un cliente puntual — se
    // carga recién al hacer clic en la fila de esa categoría (mismo patrón AJAX perezoso que ya usa
    // OnGetProductosAsync para el detalle de un cliente). categoriaCodigo llega como "" para
    // SinCategoriaCodigo (ver constante arriba) — se traduce a NULL para el filtro sobre Items.
    public async Task<IActionResult> OnGetProductosPorCategoriaAsync(string cardCode, string categoriaCodigo, CancellationToken ct)
    {
        AplicarPeriodoPorDefecto();
        await AplicarRestriccionVendedorAsync();
        var meses = GenerarMeses(Desde!.Value, Hasta!.Value);
        string? codigoFiltro = categoriaCodigo == SinCategoriaCodigo ? null : categoriaCodigo;
        var itemsDeCategoria = await _db.Items
            .Where(i => i.CategoryCode == codigoFiltro)
            .ToDictionaryAsync(i => i.ItemCode, i => i.ItemName, ct);
        // HashSet, no el Dictionary.KeyCollection directo — es el tipo que EF Core traduce de forma
        // confiable a un IN (...) parametrizado dentro de una consulta LINQ-a-SQL.
        var codigosDeCategoria = itemsDeCategoria.Keys.ToHashSet();

        // Total vendido de cada producto de esta categoría a TODOS los clientes en el período — mismo
        // criterio que el resto de "% Cartera" de esta página (sin filtro de vendedor).
        var totalPorProductoGlobal = await _db.SaleLines
            .Join(_db.Sales,
                l => new { l.DocEntry, l.SourceDocType },
                s => new { s.DocEntry, s.SourceDocType },
                (l, s) => new { s.SaleDate, l.ItemCode, l.LineTotal })
            .Where(x => x.SaleDate >= Desde && x.SaleDate <= Hasta && codigosDeCategoria.Contains(x.ItemCode))
            .GroupBy(x => x.ItemCode)
            .Select(g => new { ItemCode = g.Key, Total = g.Sum(x => x.LineTotal) })
            .ToDictionaryAsync(x => x.ItemCode, x => x.Total, ct);

        var lineasClienteQuery = _db.SaleLines
            .Join(_db.Sales,
                l => new { l.DocEntry, l.SourceDocType },
                s => new { s.DocEntry, s.SourceDocType },
                (l, s) => new { s.CardCode, s.SaleDate, s.SalesPersonCode, l.ItemCode, l.Quantity, l.LineTotal, l.GrossBuyPrice })
            .Where(x => x.SaleDate >= Desde && x.SaleDate <= Hasta && x.CardCode == cardCode
                && codigosDeCategoria.Contains(x.ItemCode));

        if (VendedorCodigo.HasValue)
        {
            lineasClienteQuery = lineasClienteQuery.Where(x => x.SalesPersonCode == VendedorCodigo);
        }

        var lineasCliente = await lineasClienteQuery
            .Select(x => new { x.SaleDate, x.ItemCode, x.Quantity, x.LineTotal, x.GrossBuyPrice })
            .ToListAsync(ct);

        var totalCategoriaCliente = lineasCliente.Sum(x => x.LineTotal);

        var productos = lineasCliente
            .GroupBy(x => x.ItemCode)
            .Select(g =>
            {
                var montoPorMes = meses.ToDictionary(
                    m => m.Etiqueta,
                    m => g.Where(x => x.SaleDate.Year == m.Anio && x.SaleDate.Month == m.Mes).Sum(x => x.LineTotal));

                var vendido = g.Sum(x => x.LineTotal);
                var costo = g.Sum(x => x.GrossBuyPrice * x.Quantity);
                var margen = vendido - costo;
                var totalGlobalProducto = totalPorProductoGlobal.GetValueOrDefault(g.Key);

                return new
                {
                    itemCode = g.Key,
                    nombre = itemsDeCategoria.GetValueOrDefault(g.Key, g.Key),
                    montoPorMes,
                    totalGeneral = vendido,
                    promedioMes = meses.Count > 0 ? vendido / meses.Count : 0m,
                    pesoProducto = totalCategoriaCliente > 0 ? vendido / totalCategoriaCliente * 100m : 0m,
                    porcentajeCartera = totalGlobalProducto > 0 ? vendido / totalGlobalProducto * 100m : 0m,
                    porcentajeMargen = vendido > 0 ? margen / vendido * 100m : 0m,
                };
            })
            .OrderByDescending(p => p.totalGeneral)
            .ToList();

        return new JsonResult(new { meses = meses.Select(m => m.Etiqueta), productos });
    }

    private void AplicarPeriodoPorDefecto()
    {
        Hasta ??= DateOnly.FromDateTime(DateTime.UtcNow);
        Desde ??= new DateOnly(Hasta.Value.Year, Hasta.Value.Month, 1).AddMonths(-11);
    }

    private static List<MesClave> GenerarMeses(DateOnly desde, DateOnly hasta)
    {
        var resultado = new List<MesClave>();
        var cursor = new DateOnly(desde.Year, desde.Month, 1);
        var limite = new DateOnly(hasta.Year, hasta.Month, 1);
        while (cursor <= limite)
        {
            resultado.Add(new MesClave(cursor.Year, cursor.Month, $"{AbreviaturasMes[cursor.Month - 1]}-{cursor.Year % 100:D2}"));
            cursor = cursor.AddMonths(1);
        }
        return resultado;
    }

    private static string NombreVendedor(int? codigo, Dictionary<int, string> nombres) =>
        codigo.HasValue && nombres.TryGetValue(codigo.Value, out var nombre) ? nombre : "—";

    public record MesClave(int Anio, int Mes, string Etiqueta);

    // Estado de cada mes al comparar contra el mismo mes del año anterior — se dibuja como un punto
    // (color + forma, no solo color) en los gráficos de Tendencia y Margen por mes.
    public enum EstadoComparacionMes { SinDatos, Superado, NoAlcanzado }

    public record PuntoTendencia(string Etiqueta, decimal Monto, EstadoComparacionMes Estado, decimal? DiferenciaAnioAnterior);

    public record PuntoMargenMensual(
        string Etiqueta,
        decimal Monto,
        decimal PorcentajeMargen,
        EstadoComparacionMes Estado,
        decimal? DiferenciaAnioAnterior);

    public record VendedorOpcion(int Codigo, string Nombre);

    public record ClienteCartera(
        string CardCode,
        string CardName,
        string Vendedor,
        Dictionary<(int Anio, int Mes), decimal> MontoPorMes,
        decimal TotalGeneral,
        decimal PromedioMes,
        decimal PesoCliente,
        decimal PorcentajeCartera,
        decimal PorcentajeMargen,
        // Desglose del mes actual (columnas Facturas/Guías/Total/% vs año anterior en la tabla) — ver
        // Insuseg.md, hallazgo de guías de despacho no facturadas (2026-08-12).
        decimal FacturasMesActual,
        decimal GuiasMesActual,
        decimal TotalMesActual,
        decimal? PorcentajeVsAnioAnterior);
}
