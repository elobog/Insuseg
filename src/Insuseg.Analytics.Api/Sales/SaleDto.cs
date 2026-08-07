namespace Insuseg.Analytics.Api.Sales;

public class SaleDto
{
    public required string CardCode { get; set; }
    public required string CardName { get; set; }
    public decimal Amount { get; set; }
    public DateOnly SaleDate { get; set; }
    public string? SalesPersonName { get; set; }

    // De qué documento SAP viene ("Order" u "Invoice") — visible por si en algún momento coexisten
    // filas de ambas fuentes (ver Insuseg.md).
    public required string SourceDocType { get; set; }
}
