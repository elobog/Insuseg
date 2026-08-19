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

    // Piso de monto POR LÍNEA (distinto de MontoMinimoVentaReal, que es sobre el DocTotal de la
    // cabecera) — mismo valor, pero es la fórmula validada contra la tabla real del cliente el
    // 2026-08-16 (6 de 7 vendedores exactos, ver Insuseg.md): una línea solo cuenta como pendiente de
    // facturar si LineStatus='bost_Open' (la cabecera puede seguir "abierta" con otras líneas ya
    // facturadas), el documento entero no es muestra/cambio/etc por texto, y LineTotal ≥ este piso.
    private const decimal MontoMinimoLineaReal = 1000m;
    private const string EstadoLineaAbierta = "bost_Open";

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

        // DeliveryNoteLines es igual de "foto del momento" que DeliveryNotes, así que se reemplaza
        // completo en cada corrida (no hay FK/cascada real entre las dos tablas — confirmado contra
        // sys.foreign_keys — así que sin este borrado explícito las líneas de guías ya facturadas o
        // canceladas quedarían huérfanas para siempre). Se reconstruye desde cero en vez de intentar
        // upsert línea por línea porque LineStatus puede pasar de abierta a cerrada sin que cambie
        // ningún otro dato de la línea — más simple y más barato que diffear.
        var lineasExistentes = await _db.DeliveryNoteLines.ToListAsync(ct);
        _db.DeliveryNoteLines.RemoveRange(lineasExistentes);

        var lineasReales = 0;
        foreach (var dto in abiertas)
        {
            if (EsTextoNoVenta(dto))
            {
                continue;
            }

            foreach (var linea in dto.DocumentLines)
            {
                if (linea.LineStatus != EstadoLineaAbierta || linea.LineTotal < MontoMinimoLineaReal)
                {
                    continue;
                }

                _db.DeliveryNoteLines.Add(new DeliveryNoteLine
                {
                    DocEntry = dto.DocEntry,
                    LineNum = linea.LineNum,
                    ItemCode = linea.ItemCode,
                    Quantity = linea.Quantity,
                    LineTotal = linea.LineTotal,
                    SalesPersonCode = linea.SalesPersonCode,
                });
                lineasReales++;
            }
        }

        await _db.SaveChangesAsync(ct);

        var reales = abiertas.Count(d => !EsMuestraOCambio(d));
        _logger.LogInformation(
            "Sincronización de guías de despacho completa: {OpenCount} abiertas ({RealCount} venta real, " +
            "{LineCount} líneas pendientes de facturar), {RemovedCount} removidas (ya facturadas/canceladas).",
            abiertas.Count, reales, lineasReales, aBorrar.Count);

        return new DeliveryNoteSyncResult
        {
            OpenCount = abiertas.Count,
            RealCount = reales,
            RemovedCount = aBorrar.Count,
            LineCount = lineasReales,
        };
    }

    // Filtro de texto a nivel de documento (Comments/NumAtCard) — SIN el piso de DocTotal, a
    // diferencia de EsMuestraOCambio: el piso real que importa para "Guías" es por línea
    // (MontoMinimoLineaReal, ver más abajo), no por el total del documento completo.
    private static bool EsTextoNoVenta(SapDeliveryNoteDto dto) =>
        (dto.Comments is not null && PatronNoVenta.IsMatch(dto.Comments)) ||
        (dto.NumAtCard is not null && PatronNoVenta.IsMatch(dto.NumAtCard));

    // Clasificación que se guarda en DeliveryNote.EsMuestraOCambio (informativa, para el resumen del
    // botón "Sincronizar ahora") — combina el filtro de texto con el piso sobre el DocTotal de la
    // cabecera. Distinta del criterio de línea (EsTextoNoVenta + MontoMinimoLineaReal) que decide qué
    // entra a DeliveryNoteLines — ver Insuseg.md 2026-08-16 sobre por qué son dos pisos separados.
    private static bool EsMuestraOCambio(SapDeliveryNoteDto dto) =>
        EsTextoNoVenta(dto) || dto.DocTotal < MontoMinimoVentaReal;
}
