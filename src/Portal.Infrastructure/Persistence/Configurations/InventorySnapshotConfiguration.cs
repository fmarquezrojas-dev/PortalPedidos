using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portal.Domain.Entities;

namespace Portal.Infrastructure.Persistence.Configurations;

public class InventorySnapshotConfiguration
    : IEntityTypeConfiguration<InventorySnapshot>
{
    public void Configure(
        EntityTypeBuilder<InventorySnapshot> builder)
    {
        builder.ToTable("InventorySnapshots", table =>
        {
            table.HasCheckConstraint(
                "CK_InventorySnapshots_Available",
                "[Available] >= 0");

            table.HasCheckConstraint(
                "CK_InventorySnapshots_Warehouse",
                "[WarehouseCode] = '01'");
        });

        builder.HasKey(snapshot => snapshot.ProductId);

        builder.Property(snapshot => snapshot.ProductId)
            .ValueGeneratedNever();

        builder.Property(snapshot => snapshot.WarehouseCode)
            .IsRequired()
            .HasMaxLength(8);

        builder.Property(snapshot => snapshot.Available)
            .HasPrecision(19, 4);

        builder.Property(snapshot => snapshot.ObservedAtUtc)
            .IsRequired();

        builder.Property(snapshot => snapshot.CatalogVersion)
            .IsRequired();

        builder.HasOne<Product>()
            .WithOne()
            .HasForeignKey<InventorySnapshot>(
                snapshot => snapshot.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}