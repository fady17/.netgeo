using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutomotiveServices.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddShopNameSnapshotsToAnonymousCartItem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ShopNameSnapshotAr",
                table: "AnonymousCartItems",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShopNameSnapshotEn",
                table: "AnonymousCartItems",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ShopNameSnapshotAr",
                table: "AnonymousCartItems");

            migrationBuilder.DropColumn(
                name: "ShopNameSnapshotEn",
                table: "AnonymousCartItems");
        }
    }
}
