using Insuseg.Analytics.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Insuseg.Analytics.Api.Pages.Compras;

[Authorize(AuthenticationSchemes = "Identity.Application")]
public class AnalisisModel : PageModel
{
    private readonly InsusegAnalyticsDbContext _db;

    public AnalisisModel(InsusegAnalyticsDbContext db)
    {
        _db = db;
    }

    [BindProperty(SupportsGet = true)]
    public DateOnly? Desde { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? Hasta { get; set; }

    public decimal TotalComprado { get; private set; }
    public int CantidadOrdenes { get; private set; }
    public ProveedorTotal? ProveedorTop { get; private set; }

    public List<ProveedorTotal> ComprasPorProveedor { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken ct)
    {
        Hasta ??= DateOnly.FromDateTime(DateTime.UtcNow);
        // Por defecto, todo el historial disponible — mismo criterio que Ventas/Analisis. Acá es
        // especialmente relevante: todo lo que hay es de 2022 o antes (ver Insuseg.md), una ventana
        // fija de días dejaría la vista siempre vacía.
        Desde ??= await _db.Purchases.MinAsync(p => (DateOnly?)p.PurchaseDate, ct) ?? Hasta.Value.AddDays(-90);

        var enPeriodo = _db.Purchases.Where(p => p.PurchaseDate >= Desde && p.PurchaseDate <= Hasta);

        var proveedorGrupos = await enPeriodo
            .GroupBy(p => new { p.CardCode, p.CardName })
            .Select(g => new { g.Key.CardCode, g.Key.CardName, Monto = g.Sum(x => x.Amount), Cantidad = g.Count() })
            .OrderByDescending(p => p.Monto)
            .ToListAsync(ct);

        // EF Core no traduce una proyección directa a un record (constructor) dentro de un GroupBy —
        // se agrupa a un tipo anónimo (sí lo traduce) y se convierte al record después, en memoria
        // (mismo workaround documentado en Ventas/Analisis).
        ComprasPorProveedor = proveedorGrupos
            .Select(p => new ProveedorTotal(p.CardCode, p.CardName, p.Monto, p.Cantidad))
            .ToList();

        TotalComprado = await enPeriodo.SumAsync(p => (decimal?)p.Amount, ct) ?? 0m;
        CantidadOrdenes = await enPeriodo.CountAsync(ct);
        ProveedorTop = ComprasPorProveedor.FirstOrDefault();
    }

    public record ProveedorTotal(string CardCode, string CardName, decimal Monto, int Cantidad);
}
