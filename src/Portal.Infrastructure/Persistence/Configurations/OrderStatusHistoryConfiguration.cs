using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portal.Domain.Entities;
using Portal.Infrastructure.Identity;

namespace Portal.Infrastructure.Persistence.Configurations;

public class OrderStatusHistoryConfiguration
    : IEntityTypeConfiguration<OrderStatusHistory>
{
    public void Configure(
        EntityTypeBuilder<OrderStatusHistory> builder)
    {
        builder.ToTable("OrderStatusHistories", table =>
        {
            table.HasCheckConstraint(
                "CK_OrderStatusHistories_PreviousStatus",
                "[PreviousStatus] IS NULL " +
                "OR [PreviousStatus] IN " +
                "('Draft', 'Queued', 'Confirmed', " +
                "'Rejected', 'Cancelled')");

            table.HasCheckConstraint(
                "CK_OrderStatusHistories_NewStatus",
                "[NewStatus] IN ('Draft', 'Queued', " +
                "'Confirmed', 'Rejected', 'Cancelled')");
        });

        builder.HasKey(history => history.Id);

        builder.Property(history => history.Id)
            .UseIdentityColumn();

        builder.Property(history => history.PreviousStatus)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(history => history.NewStatus)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(history => history.ActorUserId)
            .HasMaxLength(450);

        builder.Property(history => history.Reason)
            .IsRequired()
            .HasMaxLength(500);

        builder.HasIndex(history => new
        {
            history.OrderId,
            history.OccurredAtUtc
        });

        builder.HasIndex(history => history.CorrelationId);

        builder.HasOne<SalesOrder>()
            .WithMany()
            .HasForeignKey(history => history.OrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(history => history.ActorUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}