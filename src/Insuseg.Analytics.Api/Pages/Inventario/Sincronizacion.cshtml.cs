using Insuseg.Analytics.Data;
using Insuseg.Analytics.Data.Sync;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Insuseg.Analytics.Api.Pages.Inventario;

[Authorize(AuthenticationSchemes = "Identity.Application")]
public class SincronizacionModel : PageModel
{
    private readonly InsusegAnalyticsDbContext _db;
    private readonly InventorySyncService _syncService;

    public SincronizacionModel(InsusegAnalyticsDbContext db, InventorySyncService syncService)
    {
        _db = db;
        _syncService = syncService;
    }

    public List<ItemRow> Items { get; set; } = [];

    [TempData]
    public string? SyncSummary { get; set; }

    [TempData]
    public string? SyncError { get; set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        await LoadItemsAsync(ct);
    }

    public async Task<IActionResult> OnPostSyncAsync(CancellationToken ct)
    {
        try
        {
            var result = await _syncService.SyncAsync(ct);
            SyncSummary = $"{result.ItemCount} productos sincronizados.";
        }
        catch (Exception ex)
        {
            SyncError = $"No se pudo sincronizar: {ex.Message}";
        }

        return RedirectToPage();
    }

    private async Task LoadItemsAsync(CancellationToken ct)
    {
        // El catálogo de SAP trae miles de Items históricos sin stock (descontinuados/nunca stockeados)
        // — se ordena por stock descendente para mostrar primero lo que sí tiene existencia real, en vez
        // de una porción alfabética que en la práctica sería puro catálogo muerto.
        Items = await _db.Items
            .OrderByDescending(i => i.QuantityOnStock)
            .ThenBy(i => i.ItemCode)
            .Take(200)
            .Select(i => new ItemRow(i.ItemCode, i.ItemName, i.QuantityOnStock, i.MovingAveragePrice))
            .ToListAsync(ct);
    }

    public record ItemRow(string ItemCode, string ItemName, decimal QuantityOnStock, decimal MovingAveragePrice);
}
