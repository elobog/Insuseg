using Insuseg.Analytics.Data.Entities;

namespace Insuseg.Analytics.Data.Sync;

// Resumen de una corrida de sincronización — lo que se le muestra al usuario después de apretar
// "Sincronizar ahora", o lo que loguea la Function App cuando corre sola.
public class SalesSyncResult
{
    public required SalesSourceDocumentType Source { get; init; }
    public required int SalesPersonCount { get; init; }
    public required int DocumentCount { get; init; }
    public required int SaleLineCount { get; init; }

    // CreditNotes se sincronizan siempre además de Source, en negativo, para netear devoluciones —
    // ver SalesSyncService. Conteos aparte para que el resumen de la sincronización distinga cuánto
    // vino de cada fuente.
    public required int CreditNoteDocumentCount { get; init; }
    public required int CreditNoteLineCount { get; init; }

    public required DateOnly Since { get; init; }
    public required DateOnly Until { get; init; }
}
