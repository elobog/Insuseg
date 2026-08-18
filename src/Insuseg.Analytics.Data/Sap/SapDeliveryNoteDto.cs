namespace Insuseg.Analytics.Data.Sap;

// Guía de despacho (DeliveryNotes) — ver BDhana.md sección 8b. Solo cabecera, sin líneas: este
// proyecto usa DeliveryNotes únicamente para saber qué guías de venta real siguen sin facturar, no
// necesita el detalle de producto por línea.
public class SapDeliveryNoteDto
{
    public int DocEntry { get; set; }
    public int DocNum { get; set; }
    public DateOnly DocDate { get; set; }
    public decimal DocTotal { get; set; }
    public required string CardCode { get; set; }
    public required string CardName { get; set; }
    public int? SalesPersonCode { get; set; }
    public string? Comments { get; set; }
    public string? NumAtCard { get; set; }
}
