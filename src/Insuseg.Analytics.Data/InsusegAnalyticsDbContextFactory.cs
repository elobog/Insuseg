using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Insuseg.Analytics.Data;

// Solo se usa en tiempo de diseño (dotnet ef migrations add/update-database) para poder generar
// migraciones sin depender de un proyecto de arranque. La cadena de conexión real (Azure SQL) nunca
// va acá — se toma de INSUSEG_SQL_CONNECTION si está definida, o de un valor local sin credenciales.
public class InsusegAnalyticsDbContextFactory : IDesignTimeDbContextFactory<InsusegAnalyticsDbContext>
{
    public InsusegAnalyticsDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("INSUSEG_SQL_CONNECTION")
            ?? "Server=(local);Database=sqldb-insuseg-analytics-design;Trusted_Connection=True;TrustServerCertificate=True;";

        var optionsBuilder = new DbContextOptionsBuilder<InsusegAnalyticsDbContext>();
        optionsBuilder.UseSqlServer(connectionString);

        return new InsusegAnalyticsDbContext(optionsBuilder.Options);
    }
}
