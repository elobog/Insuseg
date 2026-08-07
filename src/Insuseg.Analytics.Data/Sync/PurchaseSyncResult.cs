namespace Insuseg.Analytics.Data.Sync;

// Resumen de una corrida de sincronización de Compras — lo que se le muestra al usuario después de
// apretar "Sincronizar ahora" en Compras/Sincronización.
public class PurchaseSyncResult
{
    public required int DocumentCount { get; init; }
}
