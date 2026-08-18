namespace Insuseg.Analytics.Data.Entities;

// Línea de detalle de una DeliveryNote — ver esa entidad. Sin FK forzada hacia Item, mismo motivo que
// SaleLine.ItemCode. Solo se guardan las líneas que sobreviven las tres reglas del modelo "correcto"
// (ver DeliveryNoteSyncService, Insuseg.md 2026-08-16): LineStatus='bost_Open' en SAP (una guía con
// DocumentStatus='bost_Open' en la cabecera puede tener líneas individuales ya facturadas), el
// documento entero no detectado como muestra/cambio/no-venta por Comments/NumAtCard, y LineTotal ≥
// $1.000 (piso de monto — los casos por debajo son consistentemente basura de $1 a $815, no ventas
// reales). El total de líneas de una guía en esta tabla ya es el saldo real pendiente de facturar.
public class DeliveryNoteLine
{
    public int DocEntry { get; set; }
    public int LineNum { get; set; }
    public required string ItemCode { get; set; }
    public decimal Quantity { get; set; }
    public decimal LineTotal { get; set; }

    // Vendedor de ESTA línea, no el de la cabecera de la guía (DeliveryNote.SalesPersonCode) — pueden
    // diferir en SAP. El modelo agrupa/filtra "Guías" por este campo, no por el de la cabecera, para
    // atribuirle el monto al vendedor correcto.
    public int? SalesPersonCode { get; set; }
}
