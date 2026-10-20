using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portal.Domain.Entities;

namespace Portal.Infrastructure.Persistence.Configurations;

public class SalesOrderLineConfiguration
    : IEntityTypeConfiguration<SalesOrderLine>
{
    public void Configure(
        EntityTypeBuilder<SalesOrderLine> builder)
    {
        builder.ToTable("SalesOrderLines", table =>
        {
            table.HasCheckConstraint(
                "CK_SalesOrderLines_Quantity",
                "[Quantity] BETWEEN 1 AND 9999 " +
                "AND [Quantity] = FLOOR([Quantity])");

            table.HasCheckConstraint(
                "CK_SalesOrderLines_UnitPrice",
                "[UnitPrice] >= 0");

            table.HasCheckConstraint(
                "CK_SalesOrderLines_LineAmount",
                "[LineAmount] >= 0");

            table.HasCheckConstraint(
                "CK_SalesOrderLines_LineNumber",
                "[LineNumber] > 0");

            table.HasCheckConstraint(
                "CK_SalesOrderLines_Warehouse",
                "[WarehouseCode] = '01'");
        });

        builder.HasKey(line => line.Id);

        builder.Property(line => line.ItemCode)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(line => line.Description)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(line => line.Quantity)
            .HasPrecision(19, 4);

        builder.Property(line => line.UnitPrice)
            .HasPrecision(19, 4);

        builder.Property(line => line.LineAmount)
            .HasPrecision(19, 2);

        builder.Property(line => line.WarehouseCode)
            .IsRequired()
            .HasMaxLength(8);

        builder.HasIndex(line => new
        {
            line.OrderId,
            line.LineNumber
        })
            .IsUnique();

        builder.HasIndex(line => new
        {
            line.OrderId,
            line.ProductId
        })
            .IsUnique();

        builder.HasOne<SalesOrder>()
            .WithMany()
            .HasForeignKey(line => line.OrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(line => line.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}