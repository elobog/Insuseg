namespace Insuseg.Analytics.Data.Sync;

// Resumen de una corrida de sincronización de Guías de despacho — lo que se le muestra al usuario
// después de apretar "Sincronizar ahora" en Ventas/Sincronización.
public class DeliveryNoteSyncResult
{
    public required int OpenCount { get; init; }
    public required int RealCount { get; init; }
    public required int RemovedCount { get; init; }
}
