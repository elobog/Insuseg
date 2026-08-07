using Insuseg.Analytics.Data.Configuration;
using Insuseg.Analytics.Data.Entities;
using Insuseg.Analytics.Data.Sap;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Insuseg.Analytics.Data.Sync;

// Lógica de sincronización SAP → Azure SQL, compartida entre la Function App (Insuseg.Analytics.Ingestion,
// corrida por horario) y la app web (Insuseg.Analytics.Api, corrida a pedido del usuario con el botón
// "Sincronizar ahora"). Un solo lugar para no duplicar la lógica de upsert entre los dos.
public class SalesSyncService
{
    // Backfill inicial (sin datos aún para esta fuente) pedido explícitamente: desde el 2024-01-01.
    private static readonly DateOnly InitialBackfillStartDate = new(2024, 1, 1);

    private readonly SapServiceLayerClient _sapClient;
    private readonly InsusegAnalyticsDbContext _db;
    private readonly SalesSourceDocumentType _source;
    private readonly ILogger<SalesSyncService> _logger;

    public SalesSyncService(
        SapServiceLayerClient sapClient,
        InsusegAnalyticsDbContext db,
        IOptions<SapServiceLayerOptions> options,
        ILogger<SalesSyncService> logger)
    {
        _sapClient = sapClient;
        _db = db;
        _source = options.Value.SalesSource;
        _logger = logger;
    }

    // forceFullResync=true ignora el watermark incremental y vuelve a pedir desde InitialBackfillStartDate
    // — pensado como acción manual y puntual (botón "Reprocesar historial completo" en Ventas/Sincronización)
    // para hacer el backfill de líneas (SaleLine) de órdenes que ya estaban sincronizadas antes de que
    // existiera ese detalle. El upsert es idempotente, así que correrlo de más no duplica ni rompe nada.
    public async Task<SalesSyncResult> SyncAsync(CancellationToken ct, bool forceFullResync = false)
    {
        _logger.LogInformation("Iniciando sincronización de ventas ({Source}) desde el Service Layer de SAP.", _source);

        var salesPersons = await _sapClient.GetSalesPersonsAsync(ct);
        await UpsertSalesPersonsAsync(salesPersons, ct);

        var until = DateOnly.FromDateTime(DateTime.UtcNow);

        var (documentCount, lineCount, since) =
            await SincronizarFuenteAsync(_source, signo: 1, forceFullResync, until, ct);

        // CreditNotes (notas de crédito) se sincroniza SIEMPRE, además de la fuente principal, en
        // negativo — devoluciones/anulaciones no tienen por qué configurarse, siempre hay que netearlas
        // contra lo vendido. Ver Cartera/Análisis: como restan directo sobre Amount/LineTotal, ninguna
        // consulta existente necesita saber que esta fuente existe.
        var (creditNoteDocumentCount, creditNoteLineCount, _) =
            await SincronizarFuenteAsync(SalesSourceDocumentType.CreditNote, signo: -1, forceFullResync, until, ct);

        _logger.LogInformation(
            "Sincronización completa: {SalesPersonCount} vendedores, {DocumentCount} documentos ({Source}), " +
            "{SaleLineCount} líneas, {CreditNoteDocumentCount} notas de crédito, {CreditNoteLineCount} líneas de " +
            "notas de crédito, entre {Since} y {Until}.",
            salesPersons.Count, documentCount, _source, lineCount, creditNoteDocumentCount, creditNoteLineCount,
            since, until);

        return new SalesSyncResult
        {
            Source = _source,
            SalesPersonCount = salesPersons.Count,
            DocumentCount = documentCount,
            SaleLineCount = lineCount,
            CreditNoteDocumentCount = creditNoteDocumentCount,
            CreditNoteLineCount = creditNoteLineCount,
            Since = since,
            Until = until,
        };
    }

    private async Task<(int DocumentCount, int LineCount, DateOnly Since)> SincronizarFuenteAsync(
        SalesSourceDocumentType type, int signo, bool forceFullResync, DateOnly until, CancellationToken ct)
    {
        var since = forceFullResync ? InitialBackfillStartDate : await GetSyncStartDateAsync(type, ct);
        var documents = await _sapClient.GetSalesDocumentsAsync(type, since, until, ct);
        await UpsertSalesAsync(type, signo, documents, ct);
        var lineCount = await UpsertSaleLinesAsync(type, signo, documents, ct);
        return (documents.Count, lineCount, since);
    }

    private async Task<DateOnly> GetSyncStartDateAsync(SalesSourceDocumentType type, CancellationToken ct)
    {
        var maxSaleDate = await _db.Sales
            .Where(s => s.SourceDocType == type)
            .MaxAsync(s => (DateOnly?)s.SaleDate, ct);
        return maxSaleDate ?? InitialBackfillStartDate;
    }

    private async Task UpsertSalesPersonsAsync(IReadOnlyList<SapSalesPersonDto> salesPersons, CancellationToken ct)
    {
        var existing = await _db.SalesPersons.ToDictionaryAsync(sp => sp.SalesEmployeeCode, ct);

        foreach (var dto in salesPersons)
        {
            if (existing.TryGetValue(dto.SalesEmployeeCode, out var entity))
            {
                entity.SalesEmployeeName = dto.SalesEmployeeName;
            }
            else
            {
                _db.SalesPersons.Add(new SalesPerson
                {
                    SalesEmployeeCode = dto.SalesEmployeeCode,
                    SalesEmployeeName = dto.SalesEmployeeName,
                });
            }
        }

        await _db.SaveChangesAsync(ct);
    }

    // signo = 1 para ventas normales, -1 para notas de crédito — así Amount/LineTotal/Quantity quedan
    // negativos en la base y cualquier .Sum() existente (Cartera, Análisis, rotación de Inventario) los
    // neta automáticamente, sin tener que filtrar por SourceDocType en cada consulta.
    private async Task UpsertSalesAsync(
        SalesSourceDocumentType type, int signo, IReadOnlyList<SapSalesDocumentDto> documents, CancellationToken ct)
    {
        if (documents.Count == 0)
        {
            return;
        }

        var docEntries = documents.Select(d => d.DocEntry).ToList();
        var existing = await _db.Sales
            .Where(s => s.SourceDocType == type && docEntries.Contains(s.DocEntry))
            .ToDictionaryAsync(s => s.DocEntry, ct);

        foreach (var dto in documents)
        {
            if (existing.TryGetValue(dto.DocEntry, out var sale))
            {
                sale.DocNum = dto.DocNum;
                sale.CardCode = dto.CardCode;
                sale.CardName = dto.CardName;
                sale.Amount = dto.DocTotal * signo;
                sale.SaleDate = dto.DocDate;
                sale.SalesPersonCode = dto.SalesPersonCode;
            }
            else
            {
                _db.Sales.Add(new Sale
                {
                    DocEntry = dto.DocEntry,
                    SourceDocType = type,
                    DocNum = dto.DocNum,
                    CardCode = dto.CardCode,
                    CardName = dto.CardName,
                    Amount = dto.DocTotal * signo,
                    SaleDate = dto.DocDate,
                    SalesPersonCode = dto.SalesPersonCode,
                });
            }
        }

        await _db.SaveChangesAsync(ct);
    }

    private async Task<int> UpsertSaleLinesAsync(
        SalesSourceDocumentType type, int signo, IReadOnlyList<SapSalesDocumentDto> documents, CancellationToken ct)
    {
        if (documents.Count == 0)
        {
            return 0;
        }

        var docEntries = documents.Select(d => d.DocEntry).ToList();
        var existing = await _db.SaleLines
            .Where(l => l.SourceDocType == type && docEntries.Contains(l.DocEntry))
            .ToDictionaryAsync(l => (l.DocEntry, l.LineNum), ct);

        var lineCount = 0;
        foreach (var doc in documents)
        {
            foreach (var line in doc.DocumentLines)
            {
                lineCount++;
                var key = (doc.DocEntry, line.LineNum);

                if (existing.TryGetValue(key, out var entity))
                {
                    entity.ItemCode = line.ItemCode;
                    entity.Quantity = line.Quantity * signo;
                    entity.LineTotal = line.LineTotal * signo;
                    entity.WarehouseCode = line.WarehouseCode;
                    entity.GrossBuyPrice = line.GrossBuyPrice;
                }
                else
                {
                    _db.SaleLines.Add(new SaleLine
                    {
                        DocEntry = doc.DocEntry,
                        SourceDocType = type,
                        LineNum = line.LineNum,
                        ItemCode = line.ItemCode,
                        Quantity = line.Quantity * signo,
                        LineTotal = line.LineTotal * signo,
                        WarehouseCode = line.WarehouseCode,
                        GrossBuyPrice = line.GrossBuyPrice,
                    });
                }
            }
        }

        await _db.SaveChangesAsync(ct);
        return lineCount;
    }
}
