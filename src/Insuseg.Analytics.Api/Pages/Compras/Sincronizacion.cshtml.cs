using Insuseg.Analytics.Data;
using Insuseg.Analytics.Data.Sync;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Insuseg.Analytics.Api.Pages.Compras;

[Authorize(AuthenticationSchemes = "Identity.Application")]
public class SincronizacionModel : PageModel
{
    private readonly InsusegAnalyticsDbContext _db;
    private readonly PurchaseSyncService _syncService;

    public SincronizacionModel(InsusegAnalyticsDbContext db, PurchaseSyncService syncService)
    {
        _db = db;
        _syncService = syncService;
    }

    public List<PurchaseRow> Purchases { get; set; } = [];

    [TempData]
    public string? SyncSummary { get; set; }

    [TempData]
    public string? SyncError { get; set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        await LoadPurchasesAsync(ct);
    }

    public async Task<IActionResult> OnPostSyncAsync(CancellationToken ct)
    {
        try
        {
            var result = await _syncService.SyncAsync(ct);
            SyncSummary = $"{result.DocumentCount} órdenes de compra sincronizadas.";
        }
        catch (Exception ex)
        {
            SyncError = $"No se pudo sincronizar: {ex.Message}";
        }

        return RedirectToPage();
    }

    private async Task LoadPurchasesAsync(CancellationToken ct)
    {
        Purchases = await _db.Purchases
            .OrderByDescending(p => p.PurchaseDate)
            .Take(200)
            .Select(p => new PurchaseRow(p.CardCode, p.CardName, p.Amount, p.PurchaseDate))
            .ToListAsync(ct);
    }

    public record PurchaseRow(string CardCode, string CardName, decimal Amount, DateOnly PurchaseDate);
}
