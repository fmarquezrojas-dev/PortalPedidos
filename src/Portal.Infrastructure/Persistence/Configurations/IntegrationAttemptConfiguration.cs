using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portal.Domain.Entities;

namespace Portal.Infrastructure.Persistence.Configurations;

public class IntegrationAttemptConfiguration
    : IEntityTypeConfiguration<IntegrationAttempt>
{
    public void Configure(
        EntityTypeBuilder<IntegrationAttempt> builder)
    {
        builder.ToTable("IntegrationAttempts", table =>
        {
            table.HasCheckConstraint(
                "CK_IntegrationAttempts_AttemptNumber",
                "[AttemptNumber] > 0");

            table.HasCheckConstraint(
                "CK_IntegrationAttempts_HttpStatus",
                "[HttpStatus] IS NULL " +
                "OR [HttpStatus] BETWEEN 100 AND 599");

            table.HasCheckConstraint(
                "CK_IntegrationAttempts_Duration",
                "[DurationMs] IS NULL OR [DurationMs] >= 0");

            table.HasCheckConstraint(
                "CK_IntegrationAttempts_FinishedAt",
                "[FinishedAtUtc] IS NULL " +
                "OR [FinishedAtUtc] >= [StartedAtUtc]");
        });

        builder.HasKey(attempt => attempt.Id);

        builder.Property(attempt => attempt.Id)
            .UseIdentityColumn();

        builder.Property(attempt => attempt.Operation)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(attempt => attempt.ResultCode)
            .IsRequired()
            .HasMaxLength(60);

        builder.Property(attempt => attempt.ErrorSummary)
            .HasMaxLength(500);

        builder.HasIndex(attempt => new
        {
            attempt.MessageId,
            attempt.AttemptNumber
        })
            .IsUnique();

        builder.HasIndex(attempt => attempt.CorrelationId);

        builder.HasOne<OutboxMessage>()
            .WithMany()
            .HasForeignKey(attempt => attempt.MessageId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}