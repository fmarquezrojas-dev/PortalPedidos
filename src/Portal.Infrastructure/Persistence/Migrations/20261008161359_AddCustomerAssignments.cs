using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Portal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CustomerAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalesTeamMemberId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssignedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    AssignedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    AssignmentReason = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    EndedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    EndedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    EndReason = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerAssignments", x => x.Id);
                    table.CheckConstraint("CK_CustomerAssignments_Closure", "([EndedAtUtc] IS NULL AND [EndedByUserId] IS NULL AND [EndReason] IS NULL) OR ([EndedAtUtc] IS NOT NULL AND [EndedByUserId] IS NOT NULL AND [EndReason] IS NOT NULL)");
                    table.CheckConstraint("CK_CustomerAssignments_Dates", "[EndedAtUtc] IS NULL OR [EndedAtUtc] >= [AssignedAtUtc]");
                    table.ForeignKey(
                        name: "FK_CustomerAssignments_AspNetUsers_AssignedByUserId",
                        column: x => x.AssignedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerAssignments_AspNetUsers_EndedByUserId",
                        column: x => x.EndedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerAssignments_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerAssignments_SalesTeamMembers_SalesTeamMemberId",
                        column: x => x.SalesTeamMemberId,
                        principalTable: "SalesTeamMembers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerAssignments_AssignedByUserId",
                table: "CustomerAssignments",
                column: "AssignedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerAssignments_CustomerId",
                table: "CustomerAssignments",
                column: "CustomerId",
                unique: true,
                filter: "[EndedAtUtc] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerAssignments_CustomerId_AssignedAtUtc",
                table: "CustomerAssignments",
                columns: new[] { "CustomerId", "AssignedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerAssignments_EndedByUserId",
                table: "CustomerAssignments",
                column: "EndedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerAssignments_SalesTeamMemberId_EndedAtUtc",
                table: "CustomerAssignments",
                columns: new[] { "SalesTeamMemberId", "EndedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomerAssignments");
        }
    }
}
