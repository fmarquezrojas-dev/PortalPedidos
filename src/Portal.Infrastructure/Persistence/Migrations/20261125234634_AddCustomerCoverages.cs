using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Portal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerCoverages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CustomerAssignmentId",
                table: "SalesOrders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CustomerCoverages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssignmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubstituteMemberId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartsAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    EndsAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    GrantedByUserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    GrantedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    RevokedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    RevokedByUserId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    RevokeReason = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerCoverages", x => x.Id);
                    table.CheckConstraint("CK_CustomerCoverages_Period", "[StartsAtUtc] < [EndsAtUtc]");
                    table.CheckConstraint("CK_CustomerCoverages_Revocation", "([RevokedAtUtc] IS NULL AND [RevokedByUserId] IS NULL AND [RevokeReason] IS NULL) OR ([RevokedAtUtc] IS NOT NULL AND [RevokedByUserId] IS NOT NULL AND [RevokeReason] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_CustomerCoverages_AspNetUsers_GrantedByUserId",
                        column: x => x.GrantedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerCoverages_AspNetUsers_RevokedByUserId",
                        column: x => x.RevokedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerCoverages_CustomerAssignments_AssignmentId",
                        column: x => x.AssignmentId,
                        principalTable: "CustomerAssignments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerCoverages_SalesTeamMembers_SubstituteMemberId",
                        column: x => x.SubstituteMemberId,
                        principalTable: "SalesTeamMembers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrders_CustomerAssignmentId",
                table: "SalesOrders",
                column: "CustomerAssignmentId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerCoverages_AssignmentId_StartsAtUtc_EndsAtUtc",
                table: "CustomerCoverages",
                columns: new[] { "AssignmentId", "StartsAtUtc", "EndsAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerCoverages_GrantedByUserId",
                table: "CustomerCoverages",
                column: "GrantedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerCoverages_RevokedByUserId",
                table: "CustomerCoverages",
                column: "RevokedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerCoverages_SubstituteMemberId",
                table: "CustomerCoverages",
                column: "SubstituteMemberId");

            migrationBuilder.AddForeignKey(
                name: "FK_SalesOrders_CustomerAssignments_CustomerAssignmentId",
                table: "SalesOrders",
                column: "CustomerAssignmentId",
                principalTable: "CustomerAssignments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SalesOrders_CustomerAssignments_CustomerAssignmentId",
                table: "SalesOrders");

            migrationBuilder.DropTable(
                name: "CustomerCoverages");

            migrationBuilder.DropIndex(
                name: "IX_SalesOrders_CustomerAssignmentId",
                table: "SalesOrders");

            migrationBuilder.DropColumn(
                name: "CustomerAssignmentId",
                table: "SalesOrders");
        }
    }
}
