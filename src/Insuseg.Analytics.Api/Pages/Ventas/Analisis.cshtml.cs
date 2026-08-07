using Insuseg.Analytics.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Insuseg.Analytics.Api.Pages.Ventas;

[Authorize(AuthenticationSchemes = "Identity.Application")]
public class AnalisisModel : PageModel
{
    // Un cliente se considera "desatendido" si no compra hace más de este umbral —
    // ajustable, es una definición de negocio, no un valor técnico.
    private const int DiasParaDesatendido = 60;

    private readonly InsusegAnalyticsDbContext _db;

    public AnalisisModel(InsusegAnalyticsDbContext db)
    {
        _db = db;
    }

    [BindProperty(SupportsGet = true)]
    public DateOnly? Desde { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? Hasta { get; set; }

    public decimal TotalVendido { get; private set; }
    public int CantidadOrdenes { get; private set; }
    public ClienteTotal? ClienteTop { get; private set; }
    public VendedorTotal? VendedorTop { get; private set; }
    public decimal MargenTotal { get; private set; }
    public decimal PorcentajeMargenPromedio { get; private set; }

    public List<ClienteTotal> VentasPorCliente { get; private set; } = [];
    public List<VendedorTotal> VentasPorVendedor { get; private set; } = [];
    public List<ClienteDesatendido> ClientesDesatendidos { get; private set; } = [];
    public List<ProductoMargen> MargenPorProducto { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken ct)
    {
        Hasta ??= DateOnly.FromDateTime(DateTime.UtcNow);
        // Por defecto, todo el historial disponible — así la primera vista no aparece vacía
        // si las ventas más recientes son más viejas que una ventana fija de N días.
        Desde ??= await _db.Sales.MinAsync(s => (DateOnly?)s.SaleDate, ct) ?? Hasta.Value.AddDays(-90);

        var vendedorNombres = await _db.SalesPersons
            .ToDictionaryAsync(sp => sp.SalesEmployeeCode, sp => sp.SalesEmployeeName, ct);

        // Todo se calcula desde las líneas (SaleLine.LineTotal), no desde Sale.Amount: Amount es el
        // DocTotal de SAP, que en Chile incluye IVA, mientras que LineTotal es el neto por línea (sin
        // IVA, estándar SAP B1) — mismo criterio ya aplicado en Ventas/Cartera. Se materializa acá
        // (ToListAsync) y el resto de las agrupaciones se hacen en memoria, para poder contar
        // documentos distintos (una orden = varias líneas) sin pelear con la traducción de EF Core.
        var lineasCrudo = await _db.SaleLines
            .Join(_db.Sales,
                l => new { l.DocEntry, l.SourceDocType },
                s => new { s.DocEntry, s.SourceDocType },
                (l, s) => new
                {
                    s.DocEntry, s.SourceDocType, s.CardCode, s.CardName, s.SaleDate, s.SalesPersonCode,
                    l.ItemCode, l.Quantity, l.LineTotal, l.GrossBuyPrice,
                })
            .Where(x => x.SaleDate >= Desde && x.SaleDate <= Hasta)
            .ToListAsync(ct);

        TotalVendido = lineasCrudo.Sum(x => x.LineTotal);
        CantidadOrdenes = lineasCrudo.Select(x => (x.DocEntry, x.SourceDocType)).Distinct().Count();

        VentasPorCliente = lineasCrudo
            .GroupBy(x => new { x.CardCode, x.CardName })
            .Select(g => new ClienteTotal(
                g.Key.CardCode,
                g.Key.CardName,
                g.Sum(x => x.LineTotal),
                g.Select(x => (x.DocEntry, x.SourceDocType)).Distinct().Count()))
            .OrderByDescending(c => c.Monto)
            .Take(15)
            .ToList();
        ClienteTop = VentasPorCliente.FirstOrDefault();

        VentasPorVendedor = lineasCrudo
            .GroupBy(x => x.SalesPersonCode)
            .Select(g => new VendedorTotal(
                NombreVendedor(g.Key, vendedorNombres),
                g.Sum(x => x.LineTotal),
                g.Select(x => (x.DocEntry, x.SourceDocType)).Distinct().Count(),
                TotalVendido > 0 ? g.Sum(x => x.LineTotal) / TotalVendido * 100m : 0m))
            .OrderByDescending(v => v.Monto)
            .ToList();
        VendedorTop = VentasPorVendedor.FirstOrDefault();

        var nombresProducto = await _db.Items.ToDictionaryAsync(i => i.ItemCode, i => i.ItemName, ct);
        var margenGrupos = lineasCrudo
            .GroupBy(x => x.ItemCode)
            .Select(g =>
            {
                var vendido = g.Sum(x => x.LineTotal);
                var costo = g.Sum(x => x.GrossBuyPrice * x.Quantity);
                var margen = vendido - costo;
                return new ProductoMargen(
                    g.Key,
                    nombresProducto.GetValueOrDefault(g.Key, g.Key),
                    g.Sum(x => x.Quantity),
                    vendido,
                    costo,
                    margen,
                    vendido > 0 ? margen / vendido * 100m : 0m);
            })
            .ToList();

        MargenTotal = margenGrupos.Sum(p => p.Margen);
        PorcentajeMargenPromedio = TotalVendido > 0 ? MargenTotal / TotalVendido * 100m : 0m;
        MargenPorProducto = margenGrupos.OrderByDescending(p => p.Margen).Take(20).ToList();

        await LoadClientesDesatendidosAsync(vendedorNombres, ct);
    }

    private async Task LoadClientesDesatendidosAsync(
        Dictionary<int, string> vendedorNombres, CancellationToken ct)
    {
        var hoy = DateOnly.FromDateTime(DateTime.UtcNow);
        var umbral = hoy.AddDays(-DiasParaDesatendido);

        // Última compra por cliente, en TODO el historial (no solo el período filtrado arriba) —
        // necesitamos saber de verdad cuándo fue la última vez que compró. Se materializa primero
        // (ToListAsync) y el filtro/proyección se hace después en memoria — encadenar más
        // operadores LINQ directo sobre "GroupBy + Select(g => g.First())" rompe la traducción a SQL.
        var ultimaCompraPorCliente = await _db.Sales
            .GroupBy(s => s.CardCode)
            .Select(g => g.OrderByDescending(s => s.SaleDate).First())
            .ToListAsync(ct);

        var ultimasCompras = ultimaCompraPorCliente
            .Where(s => s.SaleDate < umbral)
            .Select(s => new { s.CardCode, s.CardName, s.SaleDate, s.SalesPersonCode })
            .ToList();

        // Monto histórico neto (LineTotal), no Sale.Amount bruto — mismo criterio que el resto de la
        // página, sobre todo el historial (no el filtro de fecha de arriba).
        var montoHistoricoPorCliente = await _db.SaleLines
            .Join(_db.Sales,
                l => new { l.DocEntry, l.SourceDocType },
                s => new { s.DocEntry, s.SourceDocType },
                (l, s) => new { s.CardCode, l.LineTotal })
            .GroupBy(x => x.CardCode)
            .Select(g => new { CardCode = g.Key, Monto = g.Sum(x => x.LineTotal) })
            .ToDictionaryAsync(x => x.CardCode, x => x.Monto, ct);

        ClientesDesatendidos = ultimasCompras
            .Select(s => new ClienteDesatendido(
                s.CardCode,
                s.CardName,
                s.SaleDate,
                hoy.DayNumber - s.SaleDate.DayNumber,
                montoHistoricoPorCliente.GetValueOrDefault(s.CardCode),
                NombreVendedor(s.SalesPersonCode, vendedorNombres)))
            .OrderByDescending(c => c.MontoHistorico)
            .ToList();
    }

    private static string NombreVendedor(int? codigo, Dictionary<int, string> nombres) =>
        codigo.HasValue && nombres.TryGetValue(codigo.Value, out var nombre) ? nombre : "—";

    public record ClienteTotal(string CardCode, string CardName, decimal Monto, int Cantidad);

    public record VendedorTotal(string Nombre, decimal Monto, int Cantidad, decimal PorcentajeDelTotal);

    public record ClienteDesatendido(
        string CardCode, string CardName, DateOnly UltimaCompra, int DiasSinComprar,
        decimal MontoHistorico, string Vendedor);

    public record ProductoMargen(
        string ItemCode, string ItemName, decimal Unidades, decimal Vendido, decimal Costo,
        decimal Margen, decimal PorcentajeMargen);
}
