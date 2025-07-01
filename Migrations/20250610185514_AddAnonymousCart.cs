using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutomotiveServices.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAnonymousCart : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AnonymousCartItems",
                columns: table => new
                {
                    AnonymousCartItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    AnonymousUserId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ShopId = table.Column<Guid>(type: "uuid", nullable: false),
                    ShopServiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    PriceAtAddition = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    ServiceNameSnapshotEn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ServiceNameSnapshotAr = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ServiceImageUrlSnapshot = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    AddedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnonymousCartItems", x => x.AnonymousCartItemId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AnonymousCartItems_AnonUser_Shop_Service",
                table: "AnonymousCartItems",
                columns: new[] { "AnonymousUserId", "ShopId", "ShopServiceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AnonymousCartItems_AnonymousUserId",
                table: "AnonymousCartItems",
                column: "AnonymousUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AnonymousCartItems");
        }
    }
}
