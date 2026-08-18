using System.Text.RegularExpressions;
using Insuseg.Analytics.Data.Entities;
using Insuseg.Analytics.Data.Sap;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Insuseg.Analytics.Data.Sync;

// Sincronización de Guías de despacho SIN facturar SAP → Azure SQL, para el botón "Sincronizar ahora"
// de Ventas/Sincronización. A diferencia de SalesSyncService (incremental, guarda historial completo),
// esta tabla es un espejo del estado ACTUAL de SAP, sin historia — ver Insuseg.md (plan 2026-08-14):
// una guía puede pasar de "abierta" a "facturada" sin generar ningún documento nuevo, así que en cada
// corrida se trae el conjunto completo de guías abiertas y se reemplaza: se hace upsert de las que
// siguen viniendo, y se borran las que ya no aparecen (porque se facturaron o cancelaron desde la
// última corrida). No necesita fecha ni watermark — "abiertas" ya acota el conjunto.
public class DeliveryNoteSyncService
{
    // Sin campo estructurado en SAP para distinguir venta real de muestra/cambio/etc — ver BDhana.md
    // sección 8b. Palabras clave encontradas en Comments/NumAtCard sobre datos reales (2026-08-14):
    // muestras, cambios, guías internas a bordadoras (LOGO), donaciones, devoluciones por calidad,
    // consumo interno. Se complementa con un piso de monto porque no todos los casos traen una palabra
    // clave clara (ej. "INPRIMT N/V 18943", $7).
    private static readonly Regex PatronNoVenta = new(
        "MUESTRA|CAMBIO|^LOGO|INGRESO FALSO|NO FACTURAR|DONACION|CALIDAD|CONSUMO",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private const decimal MontoMinimoVentaReal = 1000m;

    private readonly SapServiceLayerClient _sapClient;
    private readonly InsusegAnalyticsDbContext _db;
    private readonly ILogger<DeliveryNoteSyncService> _logger;

    public DeliveryNoteSyncService(
        SapServiceLayerClient sapClient, InsusegAnalyticsDbContext db, ILogger<DeliveryNoteSyncService> logger)
    {
        _sapClient = sapClient;
        _db = db;
        _logger = logger;
    }

    public async Task<DeliveryNoteSyncResult> SyncAsync(CancellationToken ct)
    {
        _logger.LogInformation("Iniciando sincronización de guías de despacho abiertas desde el Service Layer de SAP.");

        var abiertas = await _sapClient.GetOpenDeliveryNotesAsync(ct);
        var docEntriesAbiertos = abiertas.Select(d => d.DocEntry).ToHashSet();

        var existentes = await _db.DeliveryNotes.ToDictionaryAsync(d => d.DocEntry, ct);

        foreach (var dto in abiertas)
        {
            var esMuestraOCambio = EsMuestraOCambio(dto);

            if (existentes.TryGetValue(dto.DocEntry, out var entity))
            {
                entity.DocNum = dto.DocNum;
                entity.CardCode = dto.CardCode;
                entity.CardName = dto.CardName;
                entity.DocDate = dto.DocDate;
                entity.SalesPersonCode = dto.SalesPersonCode;
                entity.EsMuestraOCambio = esMuestraOCambio;
            }
            else
            {
                _db.DeliveryNotes.Add(new DeliveryNote
                {
                    DocEntry = dto.DocEntry,
                    DocNum = dto.DocNum,
                    CardCode = dto.CardCode,
                    CardName = dto.CardName,
                    DocDate = dto.DocDate,
                    SalesPersonCode = dto.SalesPersonCode,
                    EsMuestraOCambio = esMuestraOCambio,
                });
            }
        }

        // Ya no está abierta en SAP (se facturó o se canceló) — se borra, no se guarda historial.
        var aBorrar = existentes.Values.Where(e => !docEntriesAbiertos.Contains(e.DocEntry)).ToList();
        _db.DeliveryNotes.RemoveRange(aBorrar);

        await _db.SaveChangesAsync(ct);

        var reales = abiertas.Count(d => !EsMuestraOCambio(d));
        _logger.LogInformation(
            "Sincronización de guías de despacho completa: {OpenCount} abiertas ({RealCount} venta real), " +
            "{RemovedCount} removidas (ya facturadas/canceladas).",
            abiertas.Count, reales, aBorrar.Count);

        return new DeliveryNoteSyncResult { OpenCount = abiertas.Count, RealCount = reales, RemovedCount = aBorrar.Count };
    }

    private static bool EsMuestraOCambio(SapDeliveryNoteDto dto) =>
        (dto.Comments is not null && PatronNoVenta.IsMatch(dto.Comments)) ||
        (dto.NumAtCard is not null && PatronNoVenta.IsMatch(dto.NumAtCard)) ||
        dto.DocTotal < MontoMinimoVentaReal;
}
