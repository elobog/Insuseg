namespace Insuseg.Analytics.Data.Sync;

// Resumen de una corrida de sincronización de Guías de despacho — lo que se le muestra al usuario
// después de apretar "Sincronizar ahora" en Ventas/Sincronización.
public class DeliveryNoteSyncResult
{
    public required int OpenCount { get; init; }
    public required int RealCount { get; init; }
    public required int RemovedCount { get; init; }

    // Líneas que pasaron las 3 reglas del modelo (LineStatus, texto no-venta, piso de monto) y
    // quedaron guardadas en DeliveryNoteLines — es lo que realmente suma la columna "Guías" de
    // Cartera, no RealCount (que es a nivel de documento completo).
    public required int LineCount { get; init; }
}
