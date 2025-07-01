using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AutomotiveServices.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddServiceModelingSchemaWithFilter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GlobalServiceDefinitions",
                columns: table => new
                {
                    GlobalServiceId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ServiceCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DefaultNameEn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DefaultNameAr = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DefaultDescriptionEn = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    DefaultDescriptionAr = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    DefaultIconUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    DefaultEstimatedDurationMinutes = table.Column<int>(type: "integer", nullable: true),
                    IsGloballyActive = table.Column<bool>(type: "boolean", nullable: false),
                    Category = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GlobalServiceDefinitions", x => x.GlobalServiceId);
                });

            migrationBuilder.CreateTable(
                name: "ShopServices",
                columns: table => new
                {
                    ShopServiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ShopId = table.Column<Guid>(type: "uuid", nullable: false),
                    GlobalServiceId = table.Column<int>(type: "integer", nullable: true),
                    CustomServiceNameEn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CustomServiceNameAr = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    EffectiveNameEn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    EffectiveNameAr = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ShopSpecificDescriptionEn = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ShopSpecificDescriptionAr = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Price = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    DurationMinutes = table.Column<int>(type: "integer", nullable: true),
                    ShopSpecificIconUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsOfferedByShop = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsPopularAtShop = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShopServices", x => x.ShopServiceId);
                    table.ForeignKey(
                        name: "FK_ShopServices_GlobalServiceDefinitions_GlobalServiceId",
                        column: x => x.GlobalServiceId,
                        principalTable: "GlobalServiceDefinitions",
                        principalColumn: "GlobalServiceId",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ShopServices_Shops_ShopId",
                        column: x => x.ShopId,
                        principalTable: "Shops",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GlobalServiceDefinitions_ServiceCode",
                table: "GlobalServiceDefinitions",
                column: "ServiceCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShopServices_GlobalServiceId",
                table: "ShopServices",
                column: "GlobalServiceId");

            migrationBuilder.CreateIndex(
                name: "IX_ShopServices_ShopId",
                table: "ShopServices",
                column: "ShopId");

            migrationBuilder.CreateIndex(
                name: "IX_ShopServices_ShopId_IsOfferedByShop",
                table: "ShopServices",
                columns: new[] { "ShopId", "IsOfferedByShop" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ShopServices");

            migrationBuilder.DropTable(
                name: "GlobalServiceDefinitions");
        }
    }
}
