using Azure.Monitor.OpenTelemetry.Exporter;
using Insuseg.Analytics.Data;
using Insuseg.Analytics.Data.Configuration;
using Insuseg.Analytics.Data.Sap;
using Insuseg.Analytics.Data.Sync;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Azure.Functions.Worker.OpenTelemetry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OpenTelemetry;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

// User Secrets solo existen en el perfil local del desarrollador — en Azure este archivo no existe,
// así que en producción la configuración debe venir de App Settings / Key Vault en su lugar.
builder.Configuration.AddUserSecrets<Program>(optional: true);

builder.Services
    .AddOptions<SapServiceLayerOptions>()
    .Bind(builder.Configuration.GetSection(SapServiceLayerOptions.SectionName))
    .ValidateOnStart();

builder.Services.AddDbContext<InsusegAnalyticsDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("InsusegAnalyticsDb"),
        // sqldb-insuseg-analytics es Serverless con auto-pausa (free tier) — la primera conexión tras
        // un período inactivo puede agotar el tiempo de espera mientras la base "despierta".
        sqlOptions => sqlOptions.EnableRetryOnFailure()));

builder.Services.AddScoped<SapServiceLayerClient>();
builder.Services.AddScoped<SalesSyncService>();

if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING")))
{
    builder.Services.AddOpenTelemetry()
        .UseFunctionsWorkerDefaults()
        .UseAzureMonitorExporter();
}

builder.Build().Run();
