using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutomotiveServices.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAdminAreaShopStatsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AdminAreaShopStats",
                columns: table => new
                {
                    AdministrativeBoundaryId = table.Column<int>(type: "integer", nullable: false),
                    ShopCount = table.Column<int>(type: "integer", nullable: false),
                    LastUpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminAreaShopStats", x => x.AdministrativeBoundaryId);
                    table.ForeignKey(
                        name: "FK_AdminAreaShopStats_AdministrativeBoundaries_AdministrativeB~",
                        column: x => x.AdministrativeBoundaryId,
                        principalTable: "AdministrativeBoundaries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AdminAreaShopStats_LastUpdatedAtUtc",
                table: "AdminAreaShopStats",
                column: "LastUpdatedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdminAreaShopStats");
        }
    }
}
