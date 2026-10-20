using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portal.Domain.Entities;

namespace Portal.Infrastructure.Persistence.Configurations;

public class OutboxMessageConfiguration
    : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("OutboxMessages", table =>
        {
            table.HasCheckConstraint(
                "CK_OutboxMessages_IdempotencyKey",
                "[IdempotencyKey] = [OrderId]");

            table.HasCheckConstraint(
                "CK_OutboxMessages_Status",
                "[Status] IN ('Pending', 'Processing', " +
                "'RetryWaiting', 'Unknown', 'Succeeded', " +
                "'Rejected', 'DeadLetter')");

            table.HasCheckConstraint(
                "CK_OutboxMessages_AttemptCount",
                "[AttemptCount] >= 0");

            table.HasCheckConstraint(
                "CK_OutboxMessages_RecoveryCycle",
                "[RecoveryCycle] >= 0");

            table.HasCheckConstraint(
                "CK_OutboxMessages_SchemaVersion",
                "[SchemaVersion] > 0");

            table.HasCheckConstraint(
                "CK_OutboxMessages_PayloadJson",
                "ISJSON([PayloadJson]) = 1");

            table.HasCheckConstraint(
                "CK_OutboxMessages_Lease",
                "([LeaseOwner] IS NULL AND [LeaseUntilUtc] IS NULL) " +
                "OR ([LeaseOwner] IS NOT NULL " +
                "AND [LeaseUntilUtc] IS NOT NULL)");
        });

        builder.HasKey(message => message.Id);

        builder.HasIndex(message => message.OrderId)
            .IsUnique();

        builder.HasIndex(message => message.IdempotencyKey)
            .IsUnique();

        builder.Property(message => message.Provider)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(message => message.PayloadJson)
            .IsRequired()
            .HasColumnType("nvarchar(max)");

        builder.Property(message => message.PayloadHash)
            .IsRequired()
            .HasColumnType("char(64)");

        builder.Property(message => message.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(message => message.LastErrorCode)
            .HasMaxLength(60);

        builder.Property(message => message.RowVersion)
            .IsRowVersion();

        builder.HasIndex(message => new
        {
            message.Status,
            message.NextAttemptAtUtc
        });

        builder.HasOne<SalesOrder>()
            .WithMany()
            .HasForeignKey(message => message.OrderId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}