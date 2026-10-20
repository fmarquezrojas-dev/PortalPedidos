using System;
using Microsoft.EntityFrameworkCore;
using ErpSimulator.Api.Entities;
using ErpSimulator.Api.Persistence.Configurations;

namespace ErpSimulator.Api.Persistence;

public class ErpSimulatorDbContext : DbContext
{
    public ErpSimulatorDbContext(
        DbContextOptions<ErpSimulatorDbContext> options)
        : base(options)
    {
    }

    // 1. Conjuntos de entidades.

    public DbSet<ErpCustomer> Customers =>
        Set<ErpCustomer>();

    public DbSet<ErpItem> Items =>
        Set<ErpItem>();

    public DbSet<ErpStock> Stocks =>
        Set<ErpStock>();

    public DbSet<ErpCatalogState> CatalogStates =>
        Set<ErpCatalogState>();

    public DbSet<ErpOrderReceipt> OrderReceipts =>
        Set<ErpOrderReceipt>();

    // 2. Convenciones generales.

    protected override void ConfigureConventions(
        ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);

        configurationBuilder
            .Properties<decimal>()
            .HavePrecision(19, 4);

        configurationBuilder
            .Properties<DateTime>()
            .HaveColumnType("datetime2(3)");
    }

    // 3. Configuración de las entidades.

    protected override void OnModelCreating(
        ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Clientes del ERP.
        modelBuilder.Entity<ErpCustomer>(entity =>
        {
            entity.ToTable("ErpCustomers");

            entity.HasKey(customer => customer.Code);

            entity.Property(customer => customer.Code)
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(customer => customer.Name)
                .HasMaxLength(200)
                .IsRequired();

            entity.Property(customer => customer.RowVersion)
                .IsRowVersion();
        });

        // Artículos del ERP.
        modelBuilder.Entity<ErpItem>(entity =>
        {
            entity.ToTable("ErpItems", table =>
            {
                table.HasCheckConstraint(
                    "CK_ErpItems_UnitPrice",
                    "[UnitPrice] >= 0 AND [UnitPrice] <= 999999.9999");
            });

            entity.HasKey(item => item.Code);

            entity.Property(item => item.Code)
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(item => item.Name)
                .HasMaxLength(200)
                .IsRequired();

            entity.Property(item => item.UnitCode)
                .HasMaxLength(20)
                .IsRequired();

            entity.Property(item => item.UnitPrice)
                .HasPrecision(19, 4);

            entity.Property(item => item.RowVersion)
                .IsRowVersion();
        });

        // Existencias por artículo y almacén.
        modelBuilder.Entity<ErpStock>(entity =>
        {
            entity.ToTable("ErpStocks", table =>
            {
                table.HasCheckConstraint(
                    "CK_ErpStocks_Quantities",
                    "[Reserved] >= 0 AND [OnHand] >= [Reserved]");

                table.HasCheckConstraint(
                    "CK_ErpStocks_Warehouse",
                    "[WarehouseCode] = '01'");
            });

            entity.HasKey(stock => new
            {
                stock.ItemCode,
                stock.WarehouseCode
            });

            entity.Property(stock => stock.ItemCode)
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(stock => stock.WarehouseCode)
                .HasMaxLength(8)
                .IsRequired();

            entity.Property(stock => stock.OnHand)
                .HasPrecision(19, 4);

            entity.Property(stock => stock.Reserved)
                .HasPrecision(19, 4);

            entity.Property(stock => stock.RowVersion)
                .IsRowVersion();

            entity.HasOne<ErpItem>()
                .WithMany()
                .HasForeignKey(stock => stock.ItemCode)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Versión global del catálogo.
        modelBuilder.Entity<ErpCatalogState>(entity =>
        {
            entity.ToTable("ErpCatalogStates", table =>
            {
                table.HasCheckConstraint(
                    "CK_ErpCatalogStates_SingleRow",
                    "[Id] = 1");

                table.HasCheckConstraint(
                    "CK_ErpCatalogStates_Version",
                    "[CurrentVersion] >= 0");
            });

            entity.HasKey(state => state.Id);

            entity.Property(state => state.Id)
                .ValueGeneratedNever();

            entity.Property(state => state.UpdatedAtUtc)
                .HasColumnType("datetime2(3)");

            entity.Property(state => state.RowVersion)
                .IsRowVersion();
        });

        // Resultados persistentes de los pedidos recibidos.
        modelBuilder.ApplyConfiguration(
            new ErpOrderReceiptConfiguration());
    }
}