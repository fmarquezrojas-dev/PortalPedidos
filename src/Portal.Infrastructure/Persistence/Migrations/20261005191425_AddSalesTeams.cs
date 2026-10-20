using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Portal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesTeams : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SalesTeams",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    SupervisorUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesTeams", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalesTeams_AspNetUsers_SupervisorUserId",
                        column: x => x.SupervisorUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SalesTeamMembers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalesTeamId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    JoinedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    LeftAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesTeamMembers", x => x.Id);
                    table.CheckConstraint("CK_SalesTeamMembers_Dates", "[LeftAtUtc] IS NULL OR [LeftAtUtc] >= [JoinedAtUtc]");
                    table.ForeignKey(
                        name: "FK_SalesTeamMembers_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesTeamMembers_SalesTeams_SalesTeamId",
                        column: x => x.SalesTeamId,
                        principalTable: "SalesTeams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SalesTeamMembers_SalesTeamId_LeftAtUtc",
                table: "SalesTeamMembers",
                columns: new[] { "SalesTeamId", "LeftAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesTeamMembers_UserId",
                table: "SalesTeamMembers",
                column: "UserId",
                unique: true,
                filter: "[LeftAtUtc] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SalesTeams_Code",
                table: "SalesTeams",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesTeams_SupervisorUserId_IsActive",
                table: "SalesTeams",
                columns: new[] { "SupervisorUserId", "IsActive" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SalesTeamMembers");

            migrationBuilder.DropTable(
                name: "SalesTeams");
        }
    }
}
