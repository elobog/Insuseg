namespace Insuseg.Analytics.Data.Sap;

// Línea de DocumentLines de una Orden/Factura — ver BDhana.md sección 4. Se pide "DocumentLines" a
// secas en $select (sin dot-path, confirmado contra el SAP real: en OData V2 un dot-path solo funciona
// sobre navigation properties expandidas con $expand, no sobre una colección de tipo complejo que ya
// viene incluida por defecto). SAP manda ~150 campos por línea; System.Text.Json ignora los que no
// mapeamos acá, así que el DTO se mantiene mínimo.
public class SapDocumentLineDto
{
    public int LineNum { get; set; }
    public required string ItemCode { get; set; }
    public decimal Quantity { get; set; }
    public decimal LineTotal { get; set; }
    public string? WarehouseCode { get; set; }

    // Costo unitario real de la línea — a diferencia de Items.MovingAveragePrice/AvgStdPrice (confirmado
    // en $0 para todo el catálogo, ver Insuseg.md), este campo estándar de SAP sí viene poblado de forma
    // confiable en Invoices.DocumentLines. Margen de la línea = LineTotal - (GrossBuyPrice * Quantity) —
    // verificado que coincide exactamente con el campo custom U_MgenMont cuando este último está poblado
    // (no siempre lo está, por eso se usa GrossBuyPrice como fuente y no U_MgenMont).
    public decimal GrossBuyPrice { get; set; }

    // "bost_Open"/"bost_Close" — estado de la línea, INDEPENDIENTE del DocumentStatus de la cabecera:
    // un documento con DocumentStatus='bost_Open' solo garantiza que AL MENOS UNA línea sigue abierta,
    // no que todas lo estén. Usado hoy solo por DeliveryNoteSyncService (ver ahí el porqué — hallazgo
    // Insuseg.md 2026-08-16, confirmado contra SAP real: 57% de las guías abiertas de la muestra tenían
    // líneas ya facturadas mezcladas con líneas realmente pendientes bajo la misma cabecera "abierta").
    public string? LineStatus { get; set; }

    // Vendedor de ESTA línea — puede diferir del vendedor de la cabecera (ver BDhana.md sección 4:
    // "SalesPersonCode (puede diferir del header, por línea)"). Usado hoy solo por
    // DeliveryNoteSyncService: el modelo de "Guías" agrupa/filtra por vendedor de línea, no de
    // cabecera, para no atribuirle a un vendedor una línea que en SAP quedó asignada a otro.
    public int? SalesPersonCode { get; set; }
}
