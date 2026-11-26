using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portal.Domain.Entities;
using Portal.Infrastructure.Identity;

namespace Portal.Infrastructure.Persistence.Configurations;

public class CustomerCoverageConfiguration : IEntityTypeConfiguration<CustomerCoverage>
{
    public void Configure(EntityTypeBuilder<CustomerCoverage> builder)
    {
        builder.ToTable("CustomerCoverages", table =>
        {
            table.HasCheckConstraint("CK_CustomerCoverages_Period", "[StartsAtUtc] < [EndsAtUtc]");
            table.HasCheckConstraint("CK_CustomerCoverages_Revocation",
                "([RevokedAtUtc] IS NULL AND [RevokedByUserId] IS NULL AND [RevokeReason] IS NULL) OR " +
                "([RevokedAtUtc] IS NOT NULL AND [RevokedByUserId] IS NOT NULL AND [RevokeReason] IS NOT NULL)");
        });
        builder.Property(c => c.Reason).HasMaxLength(250);
        builder.Property(c => c.RevokeReason).HasMaxLength(250);
        builder.HasOne<CustomerAssignment>().WithMany().HasForeignKey(c => c.AssignmentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SalesTeamMember>().WithMany().HasForeignKey(c => c.SubstituteMemberId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(c => c.GrantedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(c => c.RevokedByUserId).OnDelete(DeleteBehavior.Restrict);
        // Búsqueda de intervalos en conflicto al conceder una cobertura.
        builder.HasIndex(c => new { c.AssignmentId, c.StartsAtUtc, c.EndsAtUtc });
    }
}
