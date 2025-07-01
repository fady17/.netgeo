using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AutomotiveServices.Api.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchemaWithTablesAndViews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:postgis", ",,");

            migrationBuilder.CreateTable(
                name: "Cities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    NameEn = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    NameAr = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    StateProvince = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Country = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Location = table.Column<Point>(type: "geography(Point, 4326)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Shops",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NameEn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NameAr = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Slug = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    DescriptionEn = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    DescriptionAr = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Location = table.Column<Point>(type: "geography(Point, 4326)", nullable: false),
                    PhoneNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    ServicesOffered = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    OpeningHours = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Category = table.Column<int>(type: "integer", nullable: false),
                    CityId = table.Column<int>(type: "integer", nullable: false),
                    LogoUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Shops", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Shops_Cities_CityId",
                        column: x => x.CityId,
                        principalTable: "Cities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Cities_IsActive",
                table: "Cities",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_Cities_Location",
                table: "Cities",
                column: "Location")
                .Annotation("Npgsql:IndexMethod", "GIST");

            migrationBuilder.CreateIndex(
                name: "IX_Cities_Slug",
                table: "Cities",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Shops_Category",
                table: "Shops",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_Shops_CityId",
                table: "Shops",
                column: "CityId");

            migrationBuilder.CreateIndex(
                name: "IX_Shops_CityId_Category_IsDeleted",
                table: "Shops",
                columns: new[] { "CityId", "Category", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_Shops_CityId_Slug",
                table: "Shops",
                columns: new[] { "CityId", "Slug" },
                unique: true,
                filter: "\"Slug\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Shops_IsDeleted",
                table: "Shops",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_Shops_Location",
                table: "Shops",
                column: "Location")
                .Annotation("Npgsql:IndexMethod", "GIST");
            migrationBuilder.Sql(@"
            CREATE OR REPLACE VIEW ""CityWithCoordinates"" AS
            SELECT 
                c.""Id"",
                c.""NameEn"",
                c.""NameAr"",
                c.""Slug"",
                c.""StateProvince"",
                c.""Country"",
                c.""IsActive"",
                ST_Y(c.""Location""::geometry) as ""Latitude"",
                ST_X(c.""Location""::geometry) as ""Longitude""
            FROM ""Cities"" c;
        ");
            migrationBuilder.Sql(@"
            CREATE OR REPLACE VIEW ""ShopDetailsView"" AS
            SELECT 
                s.""Id"",
                s.""NameEn"",
                s.""NameAr"",
                s.""Slug"" AS ""ShopSlug"",
                s.""LogoUrl"",
                s.""DescriptionEn"",
                s.""DescriptionAr"",
                s.""Address"",
                s.""PhoneNumber"",
                s.""ServicesOffered"",
                s.""OpeningHours"",
                s.""Category"",         
                s.""CityId"",
                s.""IsDeleted"",
                s.""Location"",         
                ST_Y(s.""Location""::geometry) AS ""ShopLatitude"",
                ST_X(s.""Location""::geometry) AS ""ShopLongitude""
            FROM ""Shops"" s;
        ");
    }


        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP VIEW IF EXISTS ""CityWithCoordinates"";");
            migrationBuilder.Sql(@"DROP VIEW IF EXISTS ""ShopDetailsView"";");
            migrationBuilder.DropTable(
                name: "Shops");

            migrationBuilder.DropTable(
                name: "Cities");
        }
    }
}
