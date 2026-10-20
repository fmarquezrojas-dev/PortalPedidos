using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portal.Domain.Entities;
using Portal.Infrastructure.Identity;

namespace Portal.Infrastructure.Persistence.Configurations;

public class SalesOrderConfiguration
    : IEntityTypeConfiguration<SalesOrder>
{
    public void Configure(EntityTypeBuilder<SalesOrder> builder)
    {
        builder.ToTable("SalesOrders", table =>
        {
            table.HasCheckConstraint(
                "CK_SalesOrders_Status",
                "[Status] IN ('Draft', 'Queued', 'Confirmed', " +
                "'Rejected', 'Cancelled')");

            table.HasCheckConstraint(
                "CK_SalesOrders_Subtotal",
                "[Subtotal] >= 0");

            table.HasCheckConstraint(
                "CK_SalesOrders_Currency",
                "[Currency] = 'PEN'");
        });

        builder.HasKey(order => order.Id);

        builder.Property(order => order.PublicNumber)
            .HasDefaultValueSql(
                "NEXT VALUE FOR [dbo].[OrderNumbers]");

        builder.HasIndex(order => order.PublicNumber)
            .IsUnique();

        builder.Property(order => order.OwnerUserId)
            .IsRequired()
            .HasMaxLength(450);

        builder.Property(order => order.SalesTeamId)
            .IsRequired(false);

        builder.Property(order => order.CustomerCode)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(order => order.CustomerName)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(order => order.RequestedDate)
            .HasColumnType("date");

        builder.Property(order => order.Currency)
            .IsRequired()
            .HasColumnType("char(3)");

        builder.Property(order => order.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(order => order.Subtotal)
            .HasPrecision(19, 2);

        builder.Property(order => order.Provider)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(order => order.ExternalOrderId)
            .HasMaxLength(100);

        builder.Property(order => order.RowVersion)
            .IsRowVersion();

        // Consultas de pedidos por propietario.
        builder.HasIndex(order => new
        {
            order.OwnerUserId,
            order.CreatedAtUtc
        });

        // Consultas de pedidos por equipo comercial.
        builder.HasIndex(order => new
        {
            order.SalesTeamId,
            order.CreatedAtUtc
        });

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(order => order.OwnerUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // La eliminación de un equipo no debe eliminar sus pedidos.
        builder.HasOne<SalesTeam>()
            .WithMany()
            .HasForeignKey(order => order.SalesTeamId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(order => order.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<SalesOrder>()
            .WithMany()
            .HasForeignKey(order => order.ReplacesOrderId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}