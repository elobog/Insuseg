using Insuseg.Analytics.Data;
using Insuseg.Analytics.Data.Entities;
using Insuseg.Analytics.Data.Sync;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Insuseg.Analytics.Api.Pages.Ventas;

[Authorize(AuthenticationSchemes = "Identity.Application")]
public class SincronizacionModel : PageModel
{
    private readonly InsusegAnalyticsDbContext _db;
    private readonly SalesSyncService _syncService;
    private readonly InventorySyncService _inventorySyncService;
    private readonly DeliveryNoteSyncService _deliveryNoteSyncService;

    public SincronizacionModel(
        InsusegAnalyticsDbContext db,
        SalesSyncService syncService,
        InventorySyncService inventorySyncService,
        DeliveryNoteSyncService deliveryNoteSyncService)
    {
        _db = db;
        _syncService = syncService;
        _inventorySyncService = inventorySyncService;
        _deliveryNoteSyncService = deliveryNoteSyncService;
    }

    public List<SaleRow> Sales { get; set; } = [];

    [TempData]
    public string? SyncSummary { get; set; }

    [TempData]
    public string? SyncError { get; set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        await LoadSalesAsync(ct);
    }

    // Un solo botón, dos sincronizaciones — Ventas (Facturas + Notas de Crédito) y Guías de despacho
    // sin facturar corren juntas, ver Insuseg.md (plan 2026-08-14). Si la de Ventas falla, no se intenta
    // la de guías (el resumen de Ventas es el más importante); si Ventas sale bien pero Guías falla, se
    // muestra el resumen de Ventas igual y el error de Guías aparte, no se pierde lo que sí funcionó.
    public async Task<IActionResult> OnPostSyncAsync(CancellationToken ct)
    {
        try
        {
            var result = await _syncService.SyncAsync(ct);
            var resumenVentas = $"{result.DocumentCount} documentos ({result.Source}), {result.SaleLineCount} líneas, " +
                $"{result.CreditNoteDocumentCount} notas de crédito ({result.CreditNoteLineCount} líneas) y " +
                $"{result.SalesPersonCount} vendedores sincronizados. Rango: {result.Since:yyyy-MM-dd} a {result.Until:yyyy-MM-dd}.";

            try
            {
                var guias = await _deliveryNoteSyncService.SyncAsync(ct);
                SyncSummary = $"{resumenVentas} Guías de despacho sin facturar: {guias.OpenCount} " +
                    $"({guias.RealCount} venta real, {guias.OpenCount - guias.RealCount} muestra/cambio/etc — " +
                    $"{guias.RemovedCount} removidas por facturarse/cancelarse desde la última sincronización).";
            }
            catch (Exception exGuias)
            {
                SyncSummary = resumenVentas;
                SyncError = $"Ventas se sincronizó bien, pero no se pudo sincronizar guías de despacho: {exGuias.Message}";
            }
        }
        catch (Exception ex)
        {
            SyncError = $"No se pudo sincronizar: {ex.Message}";
        }

        return RedirectToPage();
    }

    // Reproceso manual y puntual: ignora el watermark incremental y vuelve a pedir todo el historial,
    // para hacer el backfill de líneas (SaleLine) de órdenes que ya estaban sincronizadas antes de que
    // existiera ese detalle (necesario para que el módulo de Inventario tenga datos de rotación).
    public async Task<IActionResult> OnPostFullResyncAsync(CancellationToken ct)
    {
        try
        {
            var result = await _syncService.SyncAsync(ct, forceFullResync: true);
            SyncSummary = $"Reproceso completo: {result.DocumentCount} documentos ({result.Source}), {result.SaleLineCount} líneas, " +
                $"{result.CreditNoteDocumentCount} notas de crédito ({result.CreditNoteLineCount} líneas) y " +
                $"{result.SalesPersonCount} vendedores. Rango: {result.Since:yyyy-MM-dd} a {result.Until:yyyy-MM-dd}.";
        }
        catch (Exception ex)
        {
            SyncError = $"No se pudo reprocesar: {ex.Message}";
        }

        return RedirectToPage();
    }

    // Compras e Inventario como módulos se borraron (2026-08-07), pero el detalle por producto de
    // Cartera todavía necesita el nombre de los ítems (tabla Items) — este botón se movió acá para
    // seguir teniéndolo sin resucitar la página de Inventario entera.
    public async Task<IActionResult> OnPostSyncProductosAsync(CancellationToken ct)
    {
        try
        {
            var result = await _inventorySyncService.SyncAsync(ct);
            SyncSummary = $"{result.ItemCount} productos y {result.CategoryCount} categorías sincronizados.";
        }
        catch (Exception ex)
        {
            SyncError = $"No se pudo sincronizar productos: {ex.Message}";
        }

        return RedirectToPage();
    }

    private async Task LoadSalesAsync(CancellationToken ct)
    {
        Sales = await _db.Sales
            .OrderByDescending(s => s.SaleDate)
            .Take(200)
            .Select(s => new SaleRow(
                s.CardCode,
                s.CardName,
                s.Amount,
                s.SaleDate,
                s.SalesPerson != null ? s.SalesPerson.SalesEmployeeName : null,
                s.SourceDocType))
            .ToListAsync(ct);
    }

    public record SaleRow(string CardCode, string CardName, decimal Amount, DateOnly SaleDate, string? SalesPersonName, SalesSourceDocumentType SourceDocType);
}
