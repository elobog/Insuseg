namespace Insuseg.Analytics.Data.Entities;

// Réplica local de un documento de venta de SAP B1 — Orden o Factura, ver SourceDocType — ver
// BDhana.md secciones 4 y 6. Clave compuesta (DocEntry, SourceDocType): DocEntry solo no alcanza
// porque no es único entre tipos de documento distintos en SAP.
public class Sale
{
    public int DocEntry { get; set; }
    public SalesSourceDocumentType SourceDocType { get; set; }
    public int DocNum { get; set; }
    public required string CardCode { get; set; }
    public required string CardName { get; set; }
    public decimal Amount { get; set; }
    public DateOnly SaleDate { get; set; }
    public int? SalesPersonCode { get; set; }
    public SalesPerson? SalesPerson { get; set; }
}
