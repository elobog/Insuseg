using Insuseg.Analytics.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Insuseg.Analytics.Api.Sales;

// Endpoint de solo lectura sobre los datos ya sincronizados en Azure SQL (nunca consulta SAP
// directamente — eso lo hace únicamente Insuseg.Analytics.Ingestion, ver Insuseg.md sección 3).
[Authorize]
[ApiController]
[Route("api/[controller]")]
public class SalesController : ControllerBase
{
    private readonly InsusegAnalyticsDbContext _db;

    public SalesController(InsusegAnalyticsDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<List<SaleDto>>> GetSales(
        [FromQuery] DateOnly? desde,
        [FromQuery] DateOnly? hasta,
        CancellationToken ct)
    {
        var query = _db.Sales.AsQueryable();

        if (desde is not null)
        {
            query = query.Where(s => s.SaleDate >= desde);
        }

        if (hasta is not null)
        {
            query = query.Where(s => s.SaleDate <= hasta);
        }

        var sales = await query
            .OrderByDescending(s => s.SaleDate)
            .Select(s => new SaleDto
            {
                CardCode = s.CardCode,
                CardName = s.CardName,
                Amount = s.Amount,
                SaleDate = s.SaleDate,
                SalesPersonName = s.SalesPerson != null ? s.SalesPerson.SalesEmployeeName : null,
                SourceDocType = s.SourceDocType.ToString(),
            })
            .ToListAsync(ct);

        return Ok(sales);
    }
}
