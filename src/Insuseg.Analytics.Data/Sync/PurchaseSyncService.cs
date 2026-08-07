using Insuseg.Analytics.Data.Entities;
using Insuseg.Analytics.Data.Sap;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Insuseg.Analytics.Data.Sync;

// Sincronización de PurchaseOrders SAP → Azure SQL, para el botón "Sincronizar ahora" de
// Compras/Sincronización. Full upsert cada corrida (no incremental por MAX(fecha) como SalesSyncService)
// — más simple, y todavía no hay evidencia de que el volumen real justifique la complejidad incremental.
public class PurchaseSyncService
{
    // Horizonte histórico de datos: desde el 1 de enero de 2024, mismo criterio para toda entidad que se
    // sincronice desde SAP (ver Insuseg.md sección 2, decisión de arquitectura 2026-07-26) — no se
    // recolecta historia anterior a esa fecha, sin importar qué tan atrás llegue el dato en SAP.
    private static readonly DateOnly HistoryStartDate = new(2024, 1, 1);

    private readonly SapServiceLayerClient _sapClient;
    private readonly InsusegAnalyticsDbContext _db;
    private readonly ILogger<PurchaseSyncService> _logger;

    public PurchaseSyncService(
        SapServiceLayerClient sapClient, InsusegAnalyticsDbContext db, ILogger<PurchaseSyncService> logger)
    {
        _sapClient = sapClient;
        _db = db;
        _logger = logger;
    }

    public async Task<PurchaseSyncResult> SyncAsync(CancellationToken ct)
    {
        _logger.LogInformation("Iniciando sincronización de compras (PurchaseOrders) desde el Service Layer de SAP.");

        var until = DateOnly.FromDateTime(DateTime.UtcNow);
        var documents = await _sapClient.GetPurchaseOrdersAsync(HistoryStartDate, until, ct);
        await UpsertPurchasesAsync(documents, ct);

        _logger.LogInformation("Sincronización de compras completa: {DocumentCount} órdenes de compra.", documents.Count);
        return new PurchaseSyncResult { DocumentCount = documents.Count };
    }

    private async Task UpsertPurchasesAsync(IReadOnlyList<SapPurchaseDocumentDto> documents, CancellationToken ct)
    {
        var existing = await _db.Purchases.ToDictionaryAsync(p => p.DocEntry, ct);

        foreach (var dto in documents)
        {
            // Confirmado contra INSUSEG real: algunas órdenes de compra no tienen proveedor asociado
            // (CardCode/CardName nulos en SAP) — se guarda un valor por defecto en vez de fallar el sync.
            var cardCode = dto.CardCode ?? "";
            var cardName = dto.CardName ?? "(Sin proveedor)";

            if (existing.TryGetValue(dto.DocEntry, out var entity))
            {
                entity.DocNum = dto.DocNum;
                entity.CardCode = cardCode;
                entity.CardName = cardName;
                entity.Amount = dto.DocTotal;
                entity.PurchaseDate = dto.DocDate;
            }
            else
            {
                _db.Purchases.Add(new Purchase
                {
                    DocEntry = dto.DocEntry,
                    DocNum = dto.DocNum,
                    CardCode = cardCode,
                    CardName = cardName,
                    Amount = dto.DocTotal,
                    PurchaseDate = dto.DocDate,
                });
            }
        }

        await _db.SaveChangesAsync(ct);
    }
}
