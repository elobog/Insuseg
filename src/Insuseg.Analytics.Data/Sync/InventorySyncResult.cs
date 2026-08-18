namespace Insuseg.Analytics.Data.Sync;

// Resumen de una corrida de sincronización de Inventario — lo que se le muestra al usuario después de
// apretar "Sincronizar ahora" en Inventario/Sincronización.
public class InventorySyncResult
{
    public required int ItemCount { get; init; }
    public required int CategoryCount { get; init; }
}
