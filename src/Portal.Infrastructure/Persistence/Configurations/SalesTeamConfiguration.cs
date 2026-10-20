using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portal.Domain.Entities;
using Portal.Infrastructure.Identity;

namespace Portal.Infrastructure.Persistence.Configurations;

public sealed class SalesTeamConfiguration
    : IEntityTypeConfiguration<SalesTeam>
{
    public void Configure(EntityTypeBuilder<SalesTeam> builder)
    {
        builder.ToTable("SalesTeams");

        builder.HasKey(team => team.Id);

        builder.Property(team => team.Code)
            .HasMaxLength(20)
            .IsRequired();

        builder.HasIndex(team => team.Code)
            .IsUnique();

        builder.Property(team => team.Name)
            .HasMaxLength(120)
            .IsRequired();

        builder.Property(team => team.SupervisorUserId)
            .HasMaxLength(450)
            .IsRequired();

        builder.Property(team => team.RowVersion)
            .IsRowVersion();

        builder.HasIndex(team => new
        {
            team.SupervisorUserId,
            team.IsActive
        });

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(team => team.SupervisorUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}