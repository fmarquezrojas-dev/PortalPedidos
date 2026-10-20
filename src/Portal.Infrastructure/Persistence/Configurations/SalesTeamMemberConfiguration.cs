using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portal.Domain.Entities;
using Portal.Infrastructure.Identity;

namespace Portal.Infrastructure.Persistence.Configurations;

public sealed class SalesTeamMemberConfiguration
    : IEntityTypeConfiguration<SalesTeamMember>
{
    public void Configure(
        EntityTypeBuilder<SalesTeamMember> builder)
    {
        builder.ToTable("SalesTeamMembers", table =>
        {
            table.HasCheckConstraint(
                "CK_SalesTeamMembers_Dates",
                "[LeftAtUtc] IS NULL OR [LeftAtUtc] >= [JoinedAtUtc]");
        });

        builder.HasKey(member => member.Id);

        builder.Property(member => member.UserId)
            .HasMaxLength(450)
            .IsRequired();

        // Un usuario solo puede tener una asignación vigente.
        builder.HasIndex(member => member.UserId)
            .IsUnique()
            .HasFilter("[LeftAtUtc] IS NULL");

        builder.HasIndex(member => new
        {
            member.SalesTeamId,
            member.LeftAtUtc
        });

        builder.HasOne<SalesTeam>()
            .WithMany()
            .HasForeignKey(member => member.SalesTeamId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(member => member.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}