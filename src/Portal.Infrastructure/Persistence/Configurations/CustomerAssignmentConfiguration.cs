using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portal.Domain.Entities;
using Portal.Infrastructure.Identity;

namespace Portal.Infrastructure.Persistence.Configurations;

public class CustomerAssignmentConfiguration
    : IEntityTypeConfiguration<CustomerAssignment>
{
    public void Configure(
        EntityTypeBuilder<CustomerAssignment> builder)
    {
        builder.ToTable("CustomerAssignments", table =>
        {
            table.HasCheckConstraint(
                "CK_CustomerAssignments_Dates",
                "[EndedAtUtc] IS NULL OR " +
                "[EndedAtUtc] >= [AssignedAtUtc]");

            table.HasCheckConstraint(
                "CK_CustomerAssignments_Closure",
                "(" +
                "[EndedAtUtc] IS NULL AND " +
                "[EndedByUserId] IS NULL AND " +
                "[EndReason] IS NULL" +
                ") OR (" +
                "[EndedAtUtc] IS NOT NULL AND " +
                "[EndedByUserId] IS NOT NULL AND " +
                "[EndReason] IS NOT NULL" +
                ")");
        });

        builder.HasKey(item => item.Id);

        builder.Property(item => item.AssignedByUserId)
            .HasMaxLength(450)
            .IsRequired();

        builder.Property(item => item.AssignmentReason)
            .HasMaxLength(250)
            .IsRequired();

        builder.Property(item => item.EndedByUserId)
            .HasMaxLength(450);

        builder.Property(item => item.EndReason)
            .HasMaxLength(250);

        builder.Property(item => item.AssignedAtUtc)
            .HasColumnType("datetime2(3)");

        builder.Property(item => item.EndedAtUtc)
            .HasColumnType("datetime2(3)");

        builder.Property(item => item.RowVersion)
            .IsRowVersion();

        // Un cliente solo puede tener una asignación abierta.
        builder.HasIndex(item => item.CustomerId)
            .IsUnique()
            .HasFilter("[EndedAtUtc] IS NULL");

        // Historial de responsables de un cliente.
        builder.HasIndex(item => new
        {
            item.CustomerId,
            item.AssignedAtUtc
        });

        // Consultas de cartera por membresía.
        builder.HasIndex(item => new
        {
            item.SalesTeamMemberId,
            item.EndedAtUtc
        });

        builder.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(item => item.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<SalesTeamMember>()
            .WithMany()
            .HasForeignKey(item => item.SalesTeamMemberId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(item => item.AssignedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(item => item.EndedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}