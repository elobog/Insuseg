using Insuseg.Analytics.Data.Sync;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Insuseg.Analytics.Ingestion;

public class SalesIngestionFunction
{
    private readonly SalesSyncService _syncService;
    private readonly ILogger<SalesIngestionFunction> _logger;

    public SalesIngestionFunction(SalesSyncService syncService, ILogger<SalesIngestionFunction> logger)
    {
        _syncService = syncService;
        _logger = logger;
    }

    [Function("SalesIngestionFunction")]
    public async Task Run([TimerTrigger("0 0 * * * *")] TimerInfo myTimer, CancellationToken ct)
    {
        try
        {
            await _syncService.SyncAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falló la sincronización de ventas programada.");
            throw;
        }
    }
}
