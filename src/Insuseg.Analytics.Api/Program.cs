using Insuseg.Analytics.Data;
using Insuseg.Analytics.Data.Configuration;
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
        // sqldb-insuseg-analytics es Serverless con auto-pausa (free tier) — la primera conexión tras
        // un período inactivo puede agotar el tiempo de espera mientras la base "despierta".
        sqlOptions => sqlOptions.EnableRetryOnFailure()));

// Endpoints /register, /login, /refresh, etc. vía ASP.NET Core Identity — ver Insuseg.md sección 2.
// También registra el esquema de cookie ("Identity.Application") que usan las páginas Razor del
// dashboard — conviven dos formas de autenticarse: Bearer para /api/*, cookie para las páginas.
builder.Services
    .AddIdentityApiEndpoints<IdentityUser>()
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
builder.Services.AddScoped<InventorySyncService>();
builder.Services.AddScoped<PurchaseSyncService>();

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
app.MapIdentityApi<IdentityUser>();
app.MapRazorPages();

// Sembrado de roles (idempotente en cada arranque, no requiere migración nueva porque
// IdentityDbContext<IdentityUser> ya incluye AspNetRoles/AspNetUserRoles con IdentityRole por
// defecto). Admin es el único rol que puede crear/borrar cuentas (ver Usuarios.cshtml.cs); las
// dos cuentas del equipo ya existentes quedan como Admin al arrancar si todavía no lo son.
using (var scope = app.Services.CreateScope())
{
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();

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

app.Run();
