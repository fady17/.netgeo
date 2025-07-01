using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AutomotiveServices.Api.Migrations
{
    /// <inheritdoc />
    public partial class LinkShopsToOperationalAreasAndAddGeoTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Shops_Cities_CityId",
                table: "Shops");

            migrationBuilder.RenameColumn(
                name: "CityId",
                table: "Shops",
                newName: "OperationalAreaId");

            migrationBuilder.RenameIndex(
                name: "IX_Shops_CityId_Slug",
                table: "Shops",
                newName: "IX_Shops_OperationalAreaId_Slug");

            migrationBuilder.RenameIndex(
                name: "IX_Shops_CityId_Category_IsDeleted",
                table: "Shops",
                newName: "IX_Shops_OperationalAreaId_Category_IsDeleted");

            migrationBuilder.RenameIndex(
                name: "IX_Shops_CityId",
                table: "Shops",
                newName: "IX_Shops_OperationalAreaId");

            migrationBuilder.CreateTable(
                name: "AdministrativeBoundaries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    NameEn = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    NameAr = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    AdminLevel = table.Column<int>(type: "integer", nullable: false),
                    ParentId = table.Column<int>(type: "integer", nullable: true),
                    CountryCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    OfficialCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Boundary = table.Column<Geometry>(type: "geography(MultiPolygon, 4326)", nullable: true),
                    SimplifiedBoundary = table.Column<Geometry>(type: "geography(MultiPolygon, 4326)", nullable: true),
                    Centroid = table.Column<Point>(type: "geography(Point, 4326)", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdministrativeBoundaries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AdministrativeBoundaries_AdministrativeBoundaries_ParentId",
                        column: x => x.ParentId,
                        principalTable: "AdministrativeBoundaries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OperationalAreas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    NameEn = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    NameAr = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Slug = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    DisplayLevel = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CentroidLatitude = table.Column<double>(type: "double precision", nullable: false),
                    CentroidLongitude = table.Column<double>(type: "double precision", nullable: false),
                    DefaultSearchRadiusMeters = table.Column<double>(type: "double precision", nullable: true),
                    DefaultMapZoomLevel = table.Column<int>(type: "integer", nullable: true),
                    GeometrySource = table.Column<int>(type: "integer", nullable: false),
                    CustomBoundary = table.Column<Geometry>(type: "geography(MultiPolygon, 4326)", nullable: true),
                    CustomSimplifiedBoundary = table.Column<Geometry>(type: "geography(MultiPolygon, 4326)", nullable: true),
                    PrimaryAdministrativeBoundaryId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperationalAreas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OperationalAreas_AdministrativeBoundaries_PrimaryAdministra~",
                        column: x => x.PrimaryAdministrativeBoundaryId,
                        principalTable: "AdministrativeBoundaries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AdministrativeBoundaries_AdminLevel",
                table: "AdministrativeBoundaries",
                column: "AdminLevel");

            migrationBuilder.CreateIndex(
                name: "IX_AdministrativeBoundaries_Boundary",
                table: "AdministrativeBoundaries",
                column: "Boundary")
                .Annotation("Npgsql:IndexMethod", "GIST");

            migrationBuilder.CreateIndex(
                name: "IX_AdministrativeBoundaries_Centroid",
                table: "AdministrativeBoundaries",
                column: "Centroid")
                .Annotation("Npgsql:IndexMethod", "GIST");

            migrationBuilder.CreateIndex(
                name: "IX_AdministrativeBoundaries_CountryCode",
                table: "AdministrativeBoundaries",
                column: "CountryCode");

            migrationBuilder.CreateIndex(
                name: "IX_AdministrativeBoundaries_IsActive",
                table: "AdministrativeBoundaries",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_AdministrativeBoundaries_ParentId",
                table: "AdministrativeBoundaries",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_AdministrativeBoundaries_SimplifiedBoundary",
                table: "AdministrativeBoundaries",
                column: "SimplifiedBoundary")
                .Annotation("Npgsql:IndexMethod", "GIST");

            migrationBuilder.CreateIndex(
                name: "IX_OperationalAreas_CustomBoundary",
                table: "OperationalAreas",
                column: "CustomBoundary")
                .Annotation("Npgsql:IndexMethod", "GIST");

            migrationBuilder.CreateIndex(
                name: "IX_OperationalAreas_CustomSimplifiedBoundary",
                table: "OperationalAreas",
                column: "CustomSimplifiedBoundary")
                .Annotation("Npgsql:IndexMethod", "GIST");

            migrationBuilder.CreateIndex(
                name: "IX_OperationalAreas_IsActive",
                table: "OperationalAreas",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_OperationalAreas_PrimaryAdministrativeBoundaryId",
                table: "OperationalAreas",
                column: "PrimaryAdministrativeBoundaryId");

            migrationBuilder.CreateIndex(
                name: "IX_OperationalAreas_Slug",
                table: "OperationalAreas",
                column: "Slug",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Shops_OperationalAreas_OperationalAreaId",
                table: "Shops",
                column: "OperationalAreaId",
                principalTable: "OperationalAreas",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Shops_OperationalAreas_OperationalAreaId",
                table: "Shops");

            migrationBuilder.DropTable(
                name: "OperationalAreas");

            migrationBuilder.DropTable(
                name: "AdministrativeBoundaries");

            migrationBuilder.RenameColumn(
                name: "OperationalAreaId",
                table: "Shops",
                newName: "CityId");

            migrationBuilder.RenameIndex(
                name: "IX_Shops_OperationalAreaId_Slug",
                table: "Shops",
                newName: "IX_Shops_CityId_Slug");

            migrationBuilder.RenameIndex(
                name: "IX_Shops_OperationalAreaId_Category_IsDeleted",
                table: "Shops",
                newName: "IX_Shops_CityId_Category_IsDeleted");

            migrationBuilder.RenameIndex(
                name: "IX_Shops_OperationalAreaId",
                table: "Shops",
                newName: "IX_Shops_CityId");

            migrationBuilder.AddForeignKey(
                name: "FK_Shops_Cities_CityId",
                table: "Shops",
                column: "CityId",
                principalTable: "Cities",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
