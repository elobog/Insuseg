namespace Insuseg.Analytics.Data.Sap;

// Subconjunto de campos traído vía $select — mismo shape para Orders (Sales Order) e Invoices (A/R
// Invoice), ver BDhana.md secciones 4 y 6. Cuál de las dos se consulta lo decide
// SapServiceLayerOptions.SalesSource (ver SapServiceLayerClient.GetSalesDocumentsAsync).
public class SapSalesDocumentDto
{
    public int DocEntry { get; set; }
    public int DocNum { get; set; }
    public DateOnly DocDate { get; set; }
    public decimal DocTotal { get; set; }
    public required string CardCode { get; set; }
    public required string CardName { get; set; }

    // -1 = "-Ningún empleado del departamento de ventas-", es un registro real en SalesPersons.
    public int? SalesPersonCode { get; set; }

    public List<SapDocumentLineDto> DocumentLines { get; set; } = [];
}
