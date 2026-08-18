using Insuseg.Analytics.Data.Entities;
using Insuseg.Analytics.Data.Sap;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Insuseg.Analytics.Data.Sync;

// Sincronización de catálogo de productos SAP → Azure SQL, para el botón "Sincronizar ahora" de
// Inventario/Sincronización. A diferencia de SalesSyncService (incremental por DocDate), Items no
// tiene una dimensión de fecha útil para acotar el pull, y es un catálogo chico — se trae completo y
// se hace upsert cada corrida (full upsert, no incremental).
public class InventorySyncService
{
    private readonly SapServiceLayerClient _sapClient;
    private readonly InsusegAnalyticsDbContext _db;
    private readonly ILogger<InventorySyncService> _logger;

    public InventorySyncService(
        SapServiceLayerClient sapClient, InsusegAnalyticsDbContext db, ILogger<InventorySyncService> logger)
    {
        _sapClient = sapClient;
        _db = db;
        _logger = logger;
    }

    public async Task<InventorySyncResult> SyncAsync(CancellationToken ct)
    {
        _logger.LogInformation("Iniciando sincronización de inventario (Items) desde el Service Layer de SAP.");

        // Categorías primero: Items.U_Categoria solo trae el código, así que el nombre ya tiene que
        // estar disponible en la tabla local antes/independiente de sincronizar Items (no hay una
        // dependencia estricta de orden a nivel de base — Item.CategoryCode no tiene FK — pero mantiene
        // la corrida coherente si algo falla a mitad de camino).
        var categorias = await _sapClient.GetItemCategoriesAsync(ct);
        await UpsertCategoriesAsync(categorias, ct);

        var items = await _sapClient.GetItemsAsync(ct);
        await UpsertItemsAsync(items, ct);

        _logger.LogInformation(
            "Sincronización de inventario completa: {ItemCount} productos, {CategoryCount} categorías.",
            items.Count, categorias.Count);
        return new InventorySyncResult { ItemCount = items.Count, CategoryCount = categorias.Count };
    }

    private async Task UpsertCategoriesAsync(IReadOnlyList<SapItemCategoryDto> categorias, CancellationToken ct)
    {
        var existing = await _db.ItemCategories.ToDictionaryAsync(c => c.Code, ct);

        foreach (var dto in categorias)
        {
            if (existing.TryGetValue(dto.Code, out var entity))
            {
                entity.Name = dto.Name;
            }
            else
            {
                _db.ItemCategories.Add(new ItemCategory { Code = dto.Code, Name = dto.Name });
            }
        }

        await _db.SaveChangesAsync(ct);
    }

    private async Task UpsertItemsAsync(IReadOnlyList<SapItemDto> items, CancellationToken ct)
    {
        var existing = await _db.Items.ToDictionaryAsync(i => i.ItemCode, ct);

        foreach (var dto in items)
        {
            if (existing.TryGetValue(dto.ItemCode, out var entity))
            {
                entity.ItemName = dto.ItemName;
                entity.ItemsGroupCode = dto.ItemsGroupCode;
                entity.QuantityOnStock = dto.QuantityOnStock;
                entity.MovingAveragePrice = dto.MovingAveragePrice;
                entity.CategoryCode = dto.U_Categoria;
            }
            else
            {
                _db.Items.Add(new Item
                {
                    ItemCode = dto.ItemCode,
                    ItemName = dto.ItemName,
                    ItemsGroupCode = dto.ItemsGroupCode,
                    QuantityOnStock = dto.QuantityOnStock,
                    MovingAveragePrice = dto.MovingAveragePrice,
                    CategoryCode = dto.U_Categoria,
                });
            }
        }

        await _db.SaveChangesAsync(ct);
    }
}
