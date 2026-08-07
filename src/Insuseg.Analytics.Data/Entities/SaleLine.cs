namespace Insuseg.Analytics.Data.Entities;

// Línea de detalle de un Sale (Orden/Factura) — ver BDhana.md sección 4. Clave compuesta
// (DocEntry, SourceDocType, LineNum), igual estilo que Sale. ItemCode es un string plano, SIN FK
// forzada hacia Item: el sync de Items (botón de Inventario) y el de Sales/líneas (botón de Ventas) se
// disparan por separado, y una FK dura podría fallar si las líneas llegan antes que el catálogo. El
// join a nombre/stock del producto se hace en memoria en la página de Análisis (mismo patrón que
// vendedorNombres en Ventas/Analisis).
public class SaleLine
{
    public int DocEntry { get; set; }
    public SalesSourceDocumentType SourceDocType { get; set; }
    public int LineNum { get; set; }
    public required string ItemCode { get; set; }
    public decimal Quantity { get; set; }
    public decimal LineTotal { get; set; }
    public string? WarehouseCode { get; set; }

    // Costo unitario real (SAP GrossBuyPrice) — ver comentario en SapDocumentLineDto. Margen de la línea
    // = LineTotal - (GrossBuyPrice * Quantity).
    public decimal GrossBuyPrice { get; set; }
}
