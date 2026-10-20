using ErpSimulator.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpSimulator.Api.Persistence.Configurations;

public sealed class ErpOrderReceiptConfiguration
    : IEntityTypeConfiguration<ErpOrderReceipt>
{
    public void Configure(
        EntityTypeBuilder<ErpOrderReceipt> builder)
    {
        builder.ToTable(
            "ErpOrderReceipts",
            "dbo",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_ErpOrderReceipts_Status",
                    "[Status] IN ('Accepted', 'Rejected')");

                table.HasCheckConstraint(
                    "CK_ErpOrderReceipts_ResultReference",
                    """
                    (
                        [Status] = 'Accepted'
                        AND [ExternalOrderId] IS NOT NULL
                        AND LEN([ExternalOrderId]) > 0
                    )
                    OR
                    (
                        [Status] = 'Rejected'
                        AND [ExternalOrderId] IS NULL
                    )
                    """);
            });

        builder.HasKey(receipt => receipt.Id);

        builder.Property(receipt => receipt.Id)
            .ValueGeneratedNever();

        builder.Property(receipt => receipt.IdempotencyKey)
            .IsRequired();

        builder.Property(receipt => receipt.ExternalReference)
            .IsRequired();

        builder.Property(receipt => receipt.PayloadHash)
            .HasMaxLength(64)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(receipt => receipt.RequestJson)
            .HasColumnType("nvarchar(max)")
            .IsRequired();

        builder.Property(receipt => receipt.Status)
            .HasMaxLength(20)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(receipt => receipt.ExternalOrderId)
            .HasMaxLength(50)
            .IsUnicode(false);

        builder.Property(receipt => receipt.ResultCode)
            .HasMaxLength(80)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(receipt => receipt.ResultMessage)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(receipt => receipt.ProcessedAtUtc)
            .HasColumnType("datetime2(3)")
            .IsRequired();

        builder.HasIndex(receipt => receipt.IdempotencyKey)
            .IsUnique();

        builder.HasIndex(receipt => receipt.ExternalReference)
            .IsUnique();

        builder.HasIndex(receipt => receipt.ExternalOrderId)
            .IsUnique()
            .HasFilter("[ExternalOrderId] IS NOT NULL");
    }
}