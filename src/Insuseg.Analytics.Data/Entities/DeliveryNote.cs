namespace Insuseg.Analytics.Data.Entities;

// Guía de despacho de SAP B1 (DeliveryNotes) que sigue SIN FACTURAR — ver BDhana.md sección 8b y
// Insuseg.md (hallazgo 2026-08-14). A propósito NO guardamos historial: esta tabla solo refleja el
// estado actual ("qué sigue pendiente de facturar hoy"), no un registro histórico de guías cerradas —
// DeliveryNoteSyncService borra cualquier fila que ya no venga en la respuesta de SAP (porque se
// facturó o se canceló desde la última sincronización).
//
// Esquema alineado al que ya existía en Azure SQL (creado por otro integrante del equipo antes de que
// este código llegara a este repo/máquina — ver Insuseg.md sección 7, nota del 2026-08-14 sobre el
// conflicto). EsMuestraOCambio se calcula en DeliveryNoteSyncService a partir de Comments/NumAtCard/
// DocTotal de SAP (texto libre, sin campo estructurado — ver BDhana.md 8b) y se guarda ya resuelto;
// el texto crudo y el monto NO se persisten acá, a propósito, para no duplicar el esquema ya en uso.
public class DeliveryNote
{
    public int DocEntry { get; set; }
    public int DocNum { get; set; }
    public required string CardCode { get; set; }
    public required string CardName { get; set; }
    public DateOnly DocDate { get; set; }
    public int? SalesPersonCode { get; set; }
    public SalesPerson? SalesPerson { get; set; }
    public bool EsMuestraOCambio { get; set; }
}
