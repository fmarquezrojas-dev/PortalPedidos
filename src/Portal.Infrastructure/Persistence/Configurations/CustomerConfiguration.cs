using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portal.Domain.Entities;

namespace Portal.Infrastructure.Persistence.Configurations;

public class CustomerConfiguration
    : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("Customers");

        builder.HasKey(customer => customer.Id);

        builder.Property(customer => customer.ExternalCode)
            .IsRequired()
            .HasMaxLength(50);

        builder.HasIndex(customer => customer.ExternalCode)
            .IsUnique();

        builder.Property(customer => customer.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(customer => customer.IsActive)
            .IsRequired();

        builder.Property(customer => customer.UpdatedAtUtc)
            .IsRequired();

        builder.Property(customer => customer.CatalogVersion)
            .IsRequired();
    }
}