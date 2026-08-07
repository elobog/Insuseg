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
}
