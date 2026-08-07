using Insuseg.Analytics.Data.Entities;

namespace Insuseg.Analytics.Data.Configuration;

// Se llena desde User Secrets en local (sección "SapServiceLayer") o desde Key Vault en producción.
// Nunca debe quedar un valor real acá ni en appsettings/local.settings.json versionado.
public class SapServiceLayerOptions
{
    public const string SectionName = "SapServiceLayer";

    public required string BaseUrl { get; set; }
    public required string CompanyDb { get; set; }
    public required string Username { get; set; }
    public required string Password { get; set; }

    // Qué documento de SAP se usa como fuente de "venta". Cambiar esto (User Secret/App Setting,
    // valor "Order" o "Invoice") es lo único que hace falta para cambiar de fuente — no requiere
    // tocar código. Ver Insuseg.md: Order por ahora, pendiente de confirmar con el cliente.
    public SalesSourceDocumentType SalesSource { get; set; } = SalesSourceDocumentType.Order;
}
