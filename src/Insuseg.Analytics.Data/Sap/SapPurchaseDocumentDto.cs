namespace Insuseg.Analytics.Data.Sap;

// Subconjunto de campos de PurchaseOrders via $select — ver BDhana.md sección 5. Misma estructura que
// SapSalesDocumentDto, sin SalesPersonCode/DocumentLines (el módulo de Compras es de gasto/proveedor,
// no de detalle por producto). CardCode/CardName referencian al proveedor, no al cliente.
//
// CardCode/CardName nullable: confirmado contra INSUSEG real (2026-07-26) que algunas órdenes de compra
// vienen sin proveedor asociado — no es un caso hipotético, rompió el sync real con una violación de NOT
// NULL antes de este ajuste. Ver PurchaseSyncService para el valor por defecto que se usa al guardar.
public class SapPurchaseDocumentDto
{
    public int DocEntry { get; set; }
    public int DocNum { get; set; }
    public DateOnly DocDate { get; set; }
    public decimal DocTotal { get; set; }
    public string? CardCode { get; set; }
    public string? CardName { get; set; }
}
