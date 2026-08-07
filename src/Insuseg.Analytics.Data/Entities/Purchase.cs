namespace Insuseg.Analytics.Data.Entities;

// Réplica local de una Orden de Compra de SAP B1 (PurchaseOrders) — ver BDhana.md sección 5. Sin
// discriminador de fuente como Sale/SourceDocType: a diferencia de Ventas, acá solo se sincroniza
// PurchaseOrders (PurchaseInvoices quedó fuera de alcance, ver Insuseg.md). CardCode/CardName
// referencian al proveedor.
public class Purchase
{
    public int DocEntry { get; set; }
    public int DocNum { get; set; }
    public required string CardCode { get; set; }
    public required string CardName { get; set; }
    public decimal Amount { get; set; }
    public DateOnly PurchaseDate { get; set; }
}
