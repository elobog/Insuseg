using Insuseg.Analytics.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Insuseg.Analytics.Api.Pages.Inventario;

[Authorize(AuthenticationSchemes = "Identity.Application")]
public class AnalisisModel : PageModel
{
    // Un producto se considera "sin movimiento" si no se vendió en los últimos N días —
    // mismo espíritu que DiasParaDesatendido en Ventas/Analisis, definición de negocio ajustable.
    private const int DiasParaSinMovimiento = 60;

    private readonly InsusegAnalyticsDbContext _db;

    public AnalisisModel(InsusegAnalyticsDbContext db)
    {
        _db = db;
    }

    [BindProperty(SupportsGet = true)]
    public DateOnly? Desde { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? Hasta { get; set; }

    public int TotalSkus { get; private set; }
    public decimal TotalUnidadesEnStock { get; private set; }
    public decimal ValorTotalInventario { get; private set; }

    public List<ProductoSinMovimiento> ProductosSinMovimiento { get; private set; } = [];
    public List<RotacionProducto> RotacionProductos { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken ct)
    {
        Hasta ??= DateOnly.FromDateTime(DateTime.UtcNow);
        // Por defecto, todo el historial disponible — mismo criterio que Ventas/Analisis, para que la
        // primera vista no aparezca vacía.
        Desde ??= await _db.Sales.MinAsync(s => (DateOnly?)s.SaleDate, ct) ?? Hasta.Value.AddDays(-90);

        // El catálogo de SAP trae ~25 mil Items históricos, la enorme mayoría sin stock (productos
        // descontinuados/nunca stockeados) — contar todos daría un número sin sentido de negocio. "SKUs
        // totales" acá significa SKUs con stock actual, consistente con las otras dos métricas.
        var conStock = _db.Items.Where(i => i.QuantityOnStock > 0);
        TotalSkus = await conStock.CountAsync(ct);
        TotalUnidadesEnStock = await conStock.SumAsync(i => (decimal?)i.QuantityOnStock, ct) ?? 0m;
        ValorTotalInventario = await conStock
            .SumAsync(i => (decimal?)(i.QuantityOnStock * i.MovingAveragePrice), ct) ?? 0m;

        await LoadProductosSinMovimientoAsync(ct);
        await LoadRotacionProductosAsync(ct);
    }

    private async Task LoadProductosSinMovimientoAsync(CancellationToken ct)
    {
        var hoy = DateOnly.FromDateTime(DateTime.UtcNow);
        var umbral = hoy.AddDays(-DiasParaSinMovimiento);

        // Última venta por producto, en TODO el historial (no el filtro de fecha de la página) — un
        // GroupBy + Max() sí traduce a SQL (a diferencia de GroupBy + Select(g => g.First()), que no).
        var ultimaVentaPorItem = await (
                from line in _db.SaleLines
                join sale in _db.Sales
                    on new { line.DocEntry, line.SourceDocType } equals new { sale.DocEntry, sale.SourceDocType }
                group sale.SaleDate by line.ItemCode into g
                select new { ItemCode = g.Key, UltimaVenta = g.Max() })
            .ToDictionaryAsync(x => x.ItemCode, x => x.UltimaVenta, ct);

        var itemsConStock = await _db.Items.Where(i => i.QuantityOnStock > 0).ToListAsync(ct);

        ProductosSinMovimiento = itemsConStock
            .Where(i => !ultimaVentaPorItem.TryGetValue(i.ItemCode, out var ultimaVenta) || ultimaVenta < umbral)
            .Select(i =>
            {
                ultimaVentaPorItem.TryGetValue(i.ItemCode, out var ultimaVenta);
                return new ProductoSinMovimiento(
                    i.ItemCode, i.ItemName, i.QuantityOnStock,
                    i.QuantityOnStock * i.MovingAveragePrice,
                    ultimaVentaPorItem.ContainsKey(i.ItemCode) ? ultimaVenta : null);
            })
            .OrderByDescending(p => p.ValorInmovilizado)
            .ToList();
    }

    private async Task LoadRotacionProductosAsync(CancellationToken ct)
    {
        var unidadesVendidasPorItem = await _db.SaleLines
            .Join(_db.Sales,
                l => new { l.DocEntry, l.SourceDocType },
                s => new { s.DocEntry, s.SourceDocType },
                (l, s) => new { l.ItemCode, l.Quantity, s.SaleDate })
            .Where(x => x.SaleDate >= Desde && x.SaleDate <= Hasta)
            .GroupBy(x => x.ItemCode)
            .Select(g => new { ItemCode = g.Key, Unidades = g.Sum(x => x.Quantity) })
            .ToDictionaryAsync(x => x.ItemCode, x => x.Unidades, ct);

        // Productos sin stock quedan fuera del ranking: dividir por cero no aplica, y es un problema
        // distinto ("falta reposición", no "sobrestock") — fuera de alcance de este módulo por ahora.
        var itemsConStock = await _db.Items.Where(i => i.QuantityOnStock > 0).ToListAsync(ct);

        RotacionProductos = itemsConStock
            .Select(i =>
            {
                var unidadesVendidas = unidadesVendidasPorItem.GetValueOrDefault(i.ItemCode);
                return new RotacionProducto(
                    i.ItemCode, i.ItemName, i.QuantityOnStock, unidadesVendidas,
                    unidadesVendidas / i.QuantityOnStock);
            })
            // Peor rotación primero: mucho stock, pocas unidades vendidas en el período.
            .OrderBy(r => r.IndiceRotacion)
            .ToList();
    }

    public record ProductoSinMovimiento(
        string ItemCode, string ItemName, decimal Stock, decimal ValorInmovilizado, DateOnly? UltimaVenta);

    public record RotacionProducto(
        string ItemCode, string ItemName, decimal Stock, decimal UnidadesVendidas, decimal IndiceRotacion);
}
