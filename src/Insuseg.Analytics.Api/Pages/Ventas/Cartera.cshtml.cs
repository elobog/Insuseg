using Insuseg.Analytics.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Insuseg.Analytics.Api.Pages.Ventas;

[Authorize(AuthenticationSchemes = "Identity.Application")]
public class CarteraModel : PageModel
{
    private static readonly string[] AbreviaturasMes =
        ["ene", "feb", "mar", "abr", "may", "jun", "jul", "ago", "sep", "oct", "nov", "dic"];

    private readonly InsusegAnalyticsDbContext _db;

    public CarteraModel(InsusegAnalyticsDbContext db)
    {
        _db = db;
    }

    [BindProperty(SupportsGet = true)]
    public DateOnly? Desde { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? Hasta { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? VendedorCodigo { get; set; }

    public List<MesClave> Meses { get; private set; } = [];
    public MesClave? MesActual { get; private set; }
    public List<VendedorOpcion> Vendedores { get; private set; } = [];
    public List<ClienteCartera> Clientes { get; private set; } = [];

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

    public async Task OnGetAsync(CancellationToken ct)
    {
        AplicarPeriodoPorDefecto();
        Meses = GenerarMeses(Desde!.Value, Hasta!.Value);
        MesActual = Meses.Count > 0 ? Meses[^1] : null;

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

        Vendedores = totalPorVendedor.Keys
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
                    (l, s) => new { s.SaleDate, s.SalesPersonCode, l.LineTotal })
                .Where(x => x.SaleDate >= inicioAnioAnterior && x.SaleDate <= finAnioAnterior);
            if (VendedorCodigo.HasValue)
            {
                queryAnioAnterior = queryAnioAnterior.Where(x => x.SalesPersonCode == VendedorCodigo);
            }

            VentaMesAnioAnterior = await queryAnioAnterior.SumAsync(x => (decimal?)x.LineTotal, ct) ?? 0m;
        }

        DiferenciaAnioAnterior = VentaMesAnioAnterior - VentaMesActual;

        MargenTotalPeriodo = lineasCrudo.Sum(v => v.LineTotal - v.GrossBuyPrice * v.Quantity);
        PorcentajeMargenPeriodo = VentaTotalPeriodo > 0 ? MargenTotalPeriodo / VentaTotalPeriodo * 100m : 0m;

        TendenciaMensual = Meses
            .Select(m => new PuntoTendencia(
                m.Etiqueta,
                lineasCrudo.Where(v => v.SaleDate.Year == m.Anio && v.SaleDate.Month == m.Mes).Sum(v => v.LineTotal)))
            .ToList();

        MargenMensual = Meses
            .Select(m =>
            {
                var lineasMes = lineasCrudo.Where(v => v.SaleDate.Year == m.Anio && v.SaleDate.Month == m.Mes).ToList();
                var ventaMes = lineasMes.Sum(v => v.LineTotal);
                var margenMes = lineasMes.Sum(v => v.LineTotal - v.GrossBuyPrice * v.Quantity);
                return new PuntoMargenMensual(
                    m.Etiqueta,
                    margenMes,
                    ventaMes > 0 ? margenMes / ventaMes * 100m : 0m);
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

                return new ClienteCartera(
                    g.Key.CardCode,
                    g.Key.CardName,
                    NombreVendedor(vendedorCodigo, vendedorNombres),
                    montoPorMes,
                    total,
                    Meses.Count > 0 ? total / Meses.Count : 0m,
                    VentaTotalPeriodo > 0 ? total / VentaTotalPeriodo * 100m : 0m,
                    totalVendedor > 0 ? total / totalVendedor * 100m : 0m,
                    total > 0 ? margen / total * 100m : 0m);
            })
            .OrderByDescending(c => c.TotalGeneral)
            .ToList();
    }

    public async Task<IActionResult> OnGetProductosAsync(string cardCode, CancellationToken ct)
    {
        AplicarPeriodoPorDefecto();
        var meses = GenerarMeses(Desde!.Value, Hasta!.Value);

        // Total vendido de cada producto a TODOS los clientes en el período — siempre sin el filtro de
        // vendedor, es el mismo criterio que el total-por-vendedor de OnGetAsync: la base de comparación
        // de "% Cartera" es una propiedad del producto/vendedor, no del filtro que esté activo.
        var totalPorProductoGlobal = await _db.SaleLines
            .Join(_db.Sales,
                l => new { l.DocEntry, l.SourceDocType },
                s => new { s.DocEntry, s.SourceDocType },
                (l, s) => new { s.SaleDate, l.ItemCode, l.LineTotal })
            .Where(x => x.SaleDate >= Desde && x.SaleDate <= Hasta)
            .GroupBy(x => x.ItemCode)
            .Select(g => new { ItemCode = g.Key, Total = g.Sum(x => x.LineTotal) })
            .ToDictionaryAsync(x => x.ItemCode, x => x.Total, ct);

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
        var nombresProducto = await _db.Items.ToDictionaryAsync(i => i.ItemCode, i => i.ItemName, ct);

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
                    nombre = nombresProducto.GetValueOrDefault(g.Key, g.Key),
                    montoPorMes,
                    totalGeneral = vendido,
                    promedioMes = meses.Count > 0 ? vendido / meses.Count : 0m,
                    pesoProducto = totalCliente > 0 ? vendido / totalCliente * 100m : 0m,
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

    public record PuntoTendencia(string Etiqueta, decimal Monto);

    public record PuntoMargenMensual(string Etiqueta, decimal Monto, decimal PorcentajeMargen);

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
        decimal PorcentajeMargen);
}
