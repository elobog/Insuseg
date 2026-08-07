namespace Insuseg.Analytics.Data.Entities;

// De qué documento SAP viene una fila de Sale. Necesario porque DocEntry NO es único entre tipos
// de documento distintos en SAP (una Orden y una Factura pueden compartir el mismo DocEntry por
// coincidencia) — sin esto, cambiar la fuente de ingesta podría mezclar filas de un documento con
// otro. La fuente principal (Order o Invoice) se configura en SapServiceLayerOptions.SalesSource;
// CreditNote se sincroniza siempre además de esa, en negativo, para netear devoluciones/anulaciones
// contra el total vendido — ver SalesSyncService.
public enum SalesSourceDocumentType
{
    Order = 0,
    Invoice = 1,
    CreditNote = 2,
}
