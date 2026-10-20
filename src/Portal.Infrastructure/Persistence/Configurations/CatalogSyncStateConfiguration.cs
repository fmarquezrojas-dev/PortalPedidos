using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portal.Domain.Entities;

namespace Portal.Infrastructure.Persistence.Configurations;

public class CatalogSyncStateConfiguration
    : IEntityTypeConfiguration<CatalogSyncState>
{
    public void Configure(
        EntityTypeBuilder<CatalogSyncState> builder)
    {
        builder.ToTable("CatalogSyncStates", table =>
        {
            table.HasCheckConstraint(
                "CK_CatalogSyncStates_SingleRecord",
                "[Id] = 1");

            table.HasCheckConstraint(
                "CK_CatalogSyncStates_CurrentVersion",
                "[CurrentVersion] >= 0");
        });

        builder.HasKey(state => state.Id);

        builder.Property(state => state.Id)
            .ValueGeneratedNever();

        builder.Property(state => state.LastErrorCode)
            .HasMaxLength(60);

        builder.Property(state => state.RowVersion)
            .IsRowVersion();
    }
}