using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portal.Domain.Entities;
using Portal.Infrastructure.Identity;

namespace Portal.Infrastructure.Persistence.Configurations;

public class AuditEventConfiguration
    : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> builder)
    {
        builder.ToTable("AuditEvents", table =>
        {
            table.HasCheckConstraint(
                "CK_AuditEvents_ActorType",
                "[ActorType] IN ('User', 'System')");

            table.HasCheckConstraint(
                "CK_AuditEvents_SummaryJson",
                "ISJSON([SummaryJson]) = 1");
        });

        builder.HasKey(audit => audit.Id);

        builder.Property(audit => audit.Id)
            .UseIdentityColumn();

        builder.Property(audit => audit.ActorUserId)
            .HasMaxLength(450);

        builder.Property(audit => audit.ActorType)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(audit => audit.Action)
            .IsRequired()
            .HasMaxLength(80);

        builder.Property(audit => audit.EntityType)
            .IsRequired()
            .HasMaxLength(80);

        builder.Property(audit => audit.EntityId)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(audit => audit.SummaryJson)
            .IsRequired()
            .HasColumnType("nvarchar(max)");

        builder.HasIndex(audit => new
        {
            audit.EntityType,
            audit.EntityId,
            audit.OccurredAtUtc
        });

        builder.HasIndex(audit => audit.CorrelationId);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(audit => audit.ActorUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}