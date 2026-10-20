using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portal.Domain.Entities;

namespace Portal.Infrastructure.Persistence.Configurations;

public class ProductConfiguration
    : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("Products", table =>
        {
            table.HasCheckConstraint(
                "CK_Products_UnitPrice",
                "[UnitPrice] >= 0");
        });

        builder.HasKey(product => product.Id);

        builder.Property(product => product.ExternalCode)
            .IsRequired()
            .HasMaxLength(50);

        builder.HasIndex(product => product.ExternalCode)
            .IsUnique();

        builder.Property(product => product.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(product => product.UnitCode)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(product => product.UnitPrice)
            .HasPrecision(19, 4);

        builder.Property(product => product.IsActive)
            .IsRequired();

        builder.Property(product => product.UpdatedAtUtc)
            .IsRequired();

        builder.Property(product => product.CatalogVersion)
            .IsRequired();
    }
}