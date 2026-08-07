using System.Net;
using System.Net.Http.Json;
using System.Net.Security;
using System.Security.Authentication;
using System.Text;
using System.Text.Json;
using Insuseg.Analytics.Data.Configuration;
using Insuseg.Analytics.Data.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Insuseg.Analytics.Data.Sap;

// Cliente de SOLO LECTURA hacia el Service Layer de SAP B1 (INSUSEG_PRB, producción — ver Insuseg.md
// sección 3). A propósito no expone ningún método de escritura sobre entidades de negocio: solo Login
// (abre sesión), GET (lee), y Logout (cierra sesión) — nunca POST/PATCH/DELETE sobre Orders,
// SalesPersons ni ninguna otra entidad de SAP.
public class SapServiceLayerClient : IAsyncDisposable
{
    private readonly SapServiceLayerOptions _options;
    private readonly ILogger<SapServiceLayerClient> _logger;
    private readonly HttpClient _httpClient;
    private bool _loggedIn;

    public SapServiceLayerClient(IOptions<SapServiceLayerOptions> options, ILogger<SapServiceLayerClient> logger)
    {
        _options = options.Value;
        _logger = logger;

        // El servidor solo acepta TLS 1.0 (ver Insuseg.md sección 4) — hay que forzarlo explícitamente,
        // .NET/Windows lo rechazan por defecto al estar deprecado.
        var handler = new SocketsHttpHandler
        {
            UseCookies = true,
            CookieContainer = new CookieContainer(),
#pragma warning disable SYSLIB0039 // Obsoleto a propósito: el servidor SAP solo acepta TLS 1.0 (ver Insuseg.md sección 4).
            SslOptions = new SslClientAuthenticationOptions
            {
                EnabledSslProtocols = SslProtocols.Tls,
                // El servidor presenta un certificado autofirmado (SEC_E_UNTRUSTED_ROOT) — es un Apache
                // on-prem en una IP fija y conocida, no un dominio público. Se acepta explícitamente
                // solo para este cliente, nunca de forma global. Ver Insuseg.md sección 4/7 (riesgo a
                // comunicar al cliente junto con el TLS 1.0 y el password débil).
                RemoteCertificateValidationCallback = (_, _, _, _) => true,
            },
#pragma warning restore SYSLIB0039
        };

        _httpClient = new HttpClient(handler) { BaseAddress = new Uri(_options.BaseUrl) };
        _httpClient.DefaultRequestHeaders.Add("Prefer", "odata.maxpagesize=100");
        // El Apache viejo que expone el Service Layer devuelve 500 genérico ante "Expect: 100-continue"
        // (header que HttpClient manda por defecto en POST) — hay que desactivarlo explícitamente.
        _httpClient.DefaultRequestHeaders.ExpectContinue = false;
    }

    public async Task<List<SapSalesPersonDto>> GetSalesPersonsAsync(CancellationToken ct)
    {
        await EnsureLoggedInAsync(ct);
        return await GetAllPagesAsync<SapSalesPersonDto>(
            "SalesPersons?$select=SalesEmployeeCode,SalesEmployeeName", ct);
    }

    public async Task<List<SapSalesDocumentDto>> GetSalesDocumentsAsync(
        SalesSourceDocumentType source, DateOnly since, DateOnly until, CancellationToken ct)
    {
        await EnsureLoggedInAsync(ct);
        var entitySet = EntitySetFor(source);
        var filter = Uri.EscapeDataString(
            $"DocDate ge '{since:yyyy-MM-dd}' and DocDate le '{until:yyyy-MM-dd}'");
        // "DocumentLines" a secas (sin dot-path) — confirmado contra el SAP real que devuelve las
        // líneas completas así (ver SapDocumentLineDto).
        return await GetAllPagesAsync<SapSalesDocumentDto>(
            $"{entitySet}?$select=DocEntry,DocNum,DocDate,DocTotal,CardCode,CardName,SalesPersonCode,DocumentLines&$filter={filter}&$orderby=DocEntry",
            ct);
    }

    public async Task<List<SapPurchaseDocumentDto>> GetPurchaseOrdersAsync(
        DateOnly since, DateOnly until, CancellationToken ct)
    {
        await EnsureLoggedInAsync(ct);
        var filter = Uri.EscapeDataString(
            $"DocDate ge '{since:yyyy-MM-dd}' and DocDate le '{until:yyyy-MM-dd}'");
        return await GetAllPagesAsync<SapPurchaseDocumentDto>(
            $"PurchaseOrders?$select=DocEntry,DocNum,DocDate,DocTotal,CardCode,CardName&$filter={filter}&$orderby=DocEntry",
            ct);
    }

    public async Task<List<SapItemDto>> GetItemsAsync(CancellationToken ct)
    {
        await EnsureLoggedInAsync(ct);
        // Valid eq 'tYES': solo artículos activos. Inclusivo a propósito (no se filtra también por
        // SalesItem) para no ocultar stock real por un recorte de alcance apresurado.
        var filter = Uri.EscapeDataString("Valid eq 'tYES'");
        return await GetAllPagesAsync<SapItemDto>(
            $"Items?$select=ItemCode,ItemName,ItemsGroupCode,QuantityOnStock,MovingAveragePrice&$filter={filter}&$orderby=ItemCode",
            ct);
    }

    // Único lugar que traduce nuestro concepto interno de "fuente de venta" al nombre real de la
    // entidad en el Service Layer — cambiar SapServiceLayerOptions.SalesSource ya alcanza para
    // apuntar a otro documento, no hace falta tocar esto.
    private static string EntitySetFor(SalesSourceDocumentType source) => source switch
    {
        SalesSourceDocumentType.Order => "Orders",
        SalesSourceDocumentType.Invoice => "Invoices",
        SalesSourceDocumentType.CreditNote => "CreditNotes",
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, null),
    };

    private async Task<List<T>> GetAllPagesAsync<T>(string relativeUrl, CancellationToken ct)
    {
        var results = new List<T>();
        string? url = relativeUrl;

        while (url is not null)
        {
            using var response = await _httpClient.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();

            var page = await response.Content.ReadFromJsonAsync<SapODataResponse<T>>(cancellationToken: ct);
            if (page?.Value is { Count: > 0 })
            {
                results.AddRange(page.Value);
            }

            url = page?.NextLink;
        }

        return results;
    }

    private async Task EnsureLoggedInAsync(CancellationToken ct)
    {
        if (_loggedIn)
        {
            return;
        }

        // StringContent (a diferencia de JsonContent/PostAsJsonAsync) calcula Content-Length de antemano
        // en vez de mandar el cuerpo con Transfer-Encoding: chunked — el Apache viejo que expone el
        // Service Layer devuelve 500 genérico ante un POST chunked.
        var loginJson = JsonSerializer.Serialize(new
        {
            CompanyDB = _options.CompanyDb,
            UserName = _options.Username,
            Password = _options.Password,
        });
        using var loginContent = new StringContent(loginJson, Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync("Login", loginContent, ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("Login al Service Layer falló ({StatusCode}): {Body}", response.StatusCode, body);
        }

        response.EnsureSuccessStatusCode();
        _loggedIn = true;
        _logger.LogInformation("Sesión iniciada contra el Service Layer de SAP ({CompanyDb}).", _options.CompanyDb);
    }

    public async ValueTask DisposeAsync()
    {
        if (_loggedIn)
        {
            try
            {
                using var response = await _httpClient.PostAsync("Logout", content: null);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No se pudo cerrar limpiamente la sesión del Service Layer de SAP.");
            }
        }

        _httpClient.Dispose();
    }
}
