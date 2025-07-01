using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutomotiveServices.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAnonymousUserPreferencesTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AnonymousUserPreferences",
                columns: table => new
                {
                    AnonymousUserPreferenceId = table.Column<Guid>(type: "uuid", nullable: false),
                    AnonymousUserId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    LastKnownLatitude = table.Column<double>(type: "double precision", nullable: true),
                    LastKnownLongitude = table.Column<double>(type: "double precision", nullable: true),
                    LastKnownLocationAccuracy = table.Column<double>(type: "double precision", nullable: true),
                    LocationSource = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    LastSetAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    OtherPreferencesJson = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnonymousUserPreferences", x => x.AnonymousUserPreferenceId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AnonymousUserPreferences_AnonymousUserId",
                table: "AnonymousUserPreferences",
                column: "AnonymousUserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AnonymousUserPreferences");
        }
    }
}
