using Insuseg.Analytics.Data.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Insuseg.Analytics.Data;

// Este DbContext apunta a sqldb-insuseg-analytics (Azure SQL), nunca a SAP.
// Hereda de IdentityDbContext para que las tablas de login (AspNetUsers, AspNetRoles, etc.) vivan
// en la misma base — ver Insuseg.md sección 2 (decisión de usar ASP.NET Core Identity).
public class InsusegAnalyticsDbContext : IdentityDbContext<IdentityUser>
{
    public InsusegAnalyticsDbContext(DbContextOptions<InsusegAnalyticsDbContext> options)
        : base(options)
    {
    }

    public DbSet<Sale> Sales => Set<Sale>();
    public DbSet<SalesPerson> SalesPersons => Set<SalesPerson>();
    public DbSet<Item> Items => Set<Item>();
    public DbSet<SaleLine> SaleLines => Set<SaleLine>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<SalesPerson>(entity =>
        {
            entity.HasKey(sp => sp.SalesEmployeeCode);
            entity.Property(sp => sp.SalesEmployeeCode).ValueGeneratedNever();
            entity.Property(sp => sp.SalesEmployeeName).HasMaxLength(100);
        });

        modelBuilder.Entity<Sale>(entity =>
        {
            entity.HasKey(s => new { s.DocEntry, s.SourceDocType });
            entity.Property(s => s.DocEntry).ValueGeneratedNever();
            entity.Property(s => s.CardCode).HasMaxLength(15);
            entity.Property(s => s.CardName).HasMaxLength(100);
            entity.Property(s => s.Amount).HasPrecision(18, 2);

            entity.HasOne(s => s.SalesPerson)
                .WithMany()
                .HasForeignKey(s => s.SalesPersonCode)
                .IsRequired(false);

            // Todas las páginas de Ventas filtran por rango de fechas (a veces sin filtrar por
            // vendedor primero) — sin este índice, cada consulta hacía un scan completo de Sales.
            entity.HasIndex(s => s.SaleDate);
        });

        modelBuilder.Entity<Item>(entity =>
        {
            entity.HasKey(i => i.ItemCode);
            entity.Property(i => i.ItemCode).HasMaxLength(50).ValueGeneratedNever();
            entity.Property(i => i.ItemName).HasMaxLength(200);
            entity.Property(i => i.QuantityOnStock).HasPrecision(18, 3);
            entity.Property(i => i.MovingAveragePrice).HasPrecision(18, 4);
        });

        modelBuilder.Entity<SaleLine>(entity =>
        {
            entity.HasKey(l => new { l.DocEntry, l.SourceDocType, l.LineNum });
            entity.Property(l => l.ItemCode).HasMaxLength(50);
            entity.Property(l => l.WarehouseCode).HasMaxLength(10);
            entity.Property(l => l.Quantity).HasPrecision(18, 3);
            entity.Property(l => l.LineTotal).HasPrecision(18, 2);
            entity.Property(l => l.GrossBuyPrice).HasPrecision(18, 4);
        });
    }
}
