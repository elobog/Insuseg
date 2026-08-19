namespace Insuseg.Analytics.Data.Sap;

// Guía de despacho (DeliveryNotes) — ver BDhana.md sección 8b. Incluye DocumentLines: el monto
// pendiente real de facturar se calcula por línea (LineStatus, piso de $1.000), no por el DocTotal
// de la cabecera — ver la fórmula validada contra el cliente en Insuseg.md (2026-08-14/16) y
// DeliveryNoteSyncService.
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
    public List<SapDocumentLineDto> DocumentLines { get; set; } = [];
}
