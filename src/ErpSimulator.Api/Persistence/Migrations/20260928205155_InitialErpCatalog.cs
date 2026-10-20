using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpSimulator.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialErpCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ErpCatalogStates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    CurrentVersion = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ErpCatalogStates", x => x.Id);
                    table.CheckConstraint("CK_ErpCatalogStates_SingleRow", "[Id] = 1");
                    table.CheckConstraint("CK_ErpCatalogStates_Version", "[CurrentVersion] >= 0");
                });

            migrationBuilder.CreateTable(
                name: "ErpCustomers",
                columns: table => new
                {
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Active = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ErpCustomers", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "ErpItems",
                columns: table => new
                {
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    UnitCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    Active = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ErpItems", x => x.Code);
                    table.CheckConstraint("CK_ErpItems_UnitPrice", "[UnitPrice] >= 0 AND [UnitPrice] <= 999999.9999");
                });

            migrationBuilder.CreateTable(
                name: "ErpStocks",
                columns: table => new
                {
                    ItemCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    WarehouseCode = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    OnHand = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    Reserved = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ErpStocks", x => new { x.ItemCode, x.WarehouseCode });
                    table.CheckConstraint("CK_ErpStocks_Quantities", "[Reserved] >= 0 AND [OnHand] >= [Reserved]");
                    table.CheckConstraint("CK_ErpStocks_Warehouse", "[WarehouseCode] = '01'");
                    table.ForeignKey(
                        name: "FK_ErpStocks_ErpItems_ItemCode",
                        column: x => x.ItemCode,
                        principalTable: "ErpItems",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Restrict);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ErpCatalogStates");

            migrationBuilder.DropTable(
                name: "ErpCustomers");

            migrationBuilder.DropTable(
                name: "ErpStocks");

            migrationBuilder.DropTable(
                name: "ErpItems");
        }
    }
}
