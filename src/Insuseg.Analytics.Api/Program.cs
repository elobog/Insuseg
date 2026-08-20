using Insuseg.Analytics.Data;
using Insuseg.Analytics.Data.Configuration;
using Insuseg.Analytics.Data.Entities;
using Insuseg.Analytics.Data.Sap;
using Insuseg.Analytics.Data.Sync;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
builder.Services.AddRazorPages();

builder.Services.AddDbContext<InsusegAnalyticsDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("InsusegAnalyticsDb"),
        // sqldb-insuseg-analytics es Serverless con auto-pausa — la primera conexión tras un período
        // inactivo NO espera sola, falla directo con el error transitorio 40613 ("Database ... is not
        // currently available") mientras la base despierta (confirmado en vivo, 2026-08-19: tardó
        // ~2-3 minutos completos en pasar de Paused a Online). El valor por defecto de
        // EnableRetryOnFailure (6 reintentos, tope de 30s cada uno) no siempre alcanza a cubrir esa
        // espera — de ahí los 500 esporádicos que veía el cliente. Con estos valores el presupuesto
        // total de reintentos cubre varios minutos, suficiente para un ciclo de resume completo.
        sqlOptions => sqlOptions.EnableRetryOnFailure(maxRetryCount: 10, maxRetryDelay: TimeSpan.FromSeconds(30), errorNumbersToAdd: null)));

// Endpoints /register, /login, /refresh, etc. vía ASP.NET Core Identity — ver Insuseg.md sección 2.
// También registra el esquema de cookie ("Identity.Application") que usan las páginas Razor del
// dashboard — conviven dos formas de autenticarse: Bearer para /api/*, cookie para las páginas.
builder.Services
    .AddIdentityApiEndpoints<ApplicationUser>()
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<InsusegAnalyticsDbContext>();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/Login";
});

builder.Services.AddAuthorization();

// Sincronización SAP → Azure SQL, para el botón "Sincronizar ahora" del dashboard — mismo servicio
// compartido que usa Insuseg.Analytics.Ingestion.
builder.Services
    .AddOptions<SapServiceLayerOptions>()
    .Bind(builder.Configuration.GetSection(SapServiceLayerOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddScoped<SapServiceLayerClient>();
builder.Services.AddScoped<SalesSyncService>();
// Compras e Inventario como módulos se borraron (2026-08-07) para enfocar todo en Cartera de
// clientes — InventorySyncService se queda igual, solo porque el detalle por producto de Cartera
// necesita el nombre de los ítems (tabla Items), que solo este servicio actualiza. El botón que lo
// dispara se movió a Ventas/Sincronización (ver Insuseg.md).
builder.Services.AddScoped<InventorySyncService>();
builder.Services.AddScoped<DeliveryNoteSyncService>();

var app = builder.Build();

// Configure the HTTP request pipeline.

app.UseHttpsRedirection();
app.UseStaticFiles();

// /register es de por sí público en MapIdentityApi — como este es un sistema interno sin alta de
// usuarios abierta, se tapa detrás de una llave que solo el equipo conoce (header
// X-Provisioning-Key, valor en User Secrets "Provisioning:RegistrationKey"). Sin la llave correcta,
// responde 404 (como si la ruta no existiera) antes de llegar al endpoint real. El resto de rutas
// de Identity (/login, /refresh) queda sin cambios.
var registrationKey = builder.Configuration["Provisioning:RegistrationKey"];
app.Use(async (context, next) =>
{
    if (HttpMethods.IsPost(context.Request.Method) &&
        context.Request.Path.Equals("/register", StringComparison.OrdinalIgnoreCase))
    {
        var providedKey = context.Request.Headers["X-Provisioning-Key"].ToString();
        if (string.IsNullOrEmpty(registrationKey) || providedKey != registrationKey)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
    }

    await next();
});

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapIdentityApi<ApplicationUser>();
app.MapRazorPages();

// Sembrado de roles (idempotente en cada arranque, no requiere migración nueva porque
// IdentityDbContext<ApplicationUser> ya incluye AspNetRoles/AspNetUserRoles con IdentityRole por
// defecto). Admin es el único rol que puede crear/borrar cuentas (ver Usuarios.cshtml.cs); las
// dos cuentas del equipo ya existentes quedan como Admin al arrancar si todavía no lo son.
//
// Con try/catch a propósito (2026-08-19): antes, cualquier error acá (p.ej. la base tardando en
// "despertar" del auto-pausado de Serverless en el primer arranque tras estar inactiva) tumbaba
// TODA la app sin dejar ningún mensaje útil en el log — Azure App Service on Linux mata el proceso
// entero (SIGABRT) ante una excepción no capturada en Main. Ahora se registra el error completo y
// la app sigue levantando igual — el sembrado de roles es idempotente, si falla una vez se reintenta
// solo en el próximo arranque/redeploy, no hace falta que bloquee el sitio completo.
try
{
    using var scope = app.Services.CreateScope();
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

    foreach (var role in new[] { "Admin", "Ejecutivo", "Vendedor" })
    {
        if (!await roleManager.RoleExistsAsync(role))
        {
            await roleManager.CreateAsync(new IdentityRole(role));
        }
    }

    foreach (var email in new[] { "info@aitbp.com", "elobog@Melirrepu.com" })
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user is not null && !await userManager.IsInRoleAsync(user, "Admin"))
        {
            await userManager.AddToRoleAsync(user, "Admin");
        }
    }
}
catch (Exception ex)
{
    app.Logger.LogError(ex, "Falló el sembrado de roles/admins al arrancar — la app sigue levantando igual.");
}

app.Run();
