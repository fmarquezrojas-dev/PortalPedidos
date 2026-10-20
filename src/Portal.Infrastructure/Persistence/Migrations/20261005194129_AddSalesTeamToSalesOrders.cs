using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Portal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesTeamToSalesOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SalesTeamId",
                table: "SalesOrders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrders_SalesTeamId_CreatedAtUtc",
                table: "SalesOrders",
                columns: new[] { "SalesTeamId", "CreatedAtUtc" });

            migrationBuilder.AddForeignKey(
                name: "FK_SalesOrders_SalesTeams_SalesTeamId",
                table: "SalesOrders",
                column: "SalesTeamId",
                principalTable: "SalesTeams",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SalesOrders_SalesTeams_SalesTeamId",
                table: "SalesOrders");

            migrationBuilder.DropIndex(
                name: "IX_SalesOrders_SalesTeamId_CreatedAtUtc",
                table: "SalesOrders");

            migrationBuilder.DropColumn(
                name: "SalesTeamId",
                table: "SalesOrders");
        }
    }
}
