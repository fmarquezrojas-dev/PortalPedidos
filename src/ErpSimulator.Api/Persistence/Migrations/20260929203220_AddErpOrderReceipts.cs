using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpSimulator.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddErpOrderReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "dbo");

            migrationBuilder.CreateTable(
                name: "ErpOrderReceipts",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdempotencyKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExternalReference = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayloadHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    RequestJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    ExternalOrderId = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    ResultCode = table.Column<string>(type: "varchar(80)", unicode: false, maxLength: 80, nullable: false),
                    ResultMessage = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ProcessedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ErpOrderReceipts", x => x.Id);
                    table.CheckConstraint("CK_ErpOrderReceipts_ResultReference", "(\r\n    [Status] = 'Accepted'\r\n    AND [ExternalOrderId] IS NOT NULL\r\n    AND LEN([ExternalOrderId]) > 0\r\n)\r\nOR\r\n(\r\n    [Status] = 'Rejected'\r\n    AND [ExternalOrderId] IS NULL\r\n)");
                    table.CheckConstraint("CK_ErpOrderReceipts_Status", "[Status] IN ('Accepted', 'Rejected')");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ErpOrderReceipts_ExternalOrderId",
                schema: "dbo",
                table: "ErpOrderReceipts",
                column: "ExternalOrderId",
                unique: true,
                filter: "[ExternalOrderId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ErpOrderReceipts_ExternalReference",
                schema: "dbo",
                table: "ErpOrderReceipts",
                column: "ExternalReference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ErpOrderReceipts_IdempotencyKey",
                schema: "dbo",
                table: "ErpOrderReceipts",
                column: "IdempotencyKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ErpOrderReceipts",
                schema: "dbo");
        }
    }
}
