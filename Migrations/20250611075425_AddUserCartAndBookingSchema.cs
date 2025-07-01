using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutomotiveServices.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddUserCartAndBookingSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Bookings",
                columns: table => new
                {
                    BookingId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ShopId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    TotalAmountAtBooking = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    PreferredDateTimeNotes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    UserContactPhoneNumber = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    UserContactEmail = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConfirmedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Bookings", x => x.BookingId);
                    table.ForeignKey(
                        name: "FK_Bookings_Shops_ShopId",
                        column: x => x.ShopId,
                        principalTable: "Shops",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UserCartItems",
                columns: table => new
                {
                    UserCartItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ShopId = table.Column<Guid>(type: "uuid", nullable: false),
                    ShopServiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    PriceAtAddition = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    ServiceNameSnapshotEn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ServiceNameSnapshotAr = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ShopNameSnapshotEn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ShopNameSnapshotAr = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ServiceImageUrlSnapshot = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    AddedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserCartItems", x => x.UserCartItemId);
                });

            migrationBuilder.CreateTable(
                name: "BookingItems",
                columns: table => new
                {
                    BookingItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    BookingId = table.Column<Guid>(type: "uuid", nullable: false),
                    ShopServiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceNameSnapshotEn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ServiceNameSnapshotAr = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ShopNameSnapshotEn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ShopNameSnapshotAr = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    PriceAtBooking = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookingItems", x => x.BookingItemId);
                    table.ForeignKey(
                        name: "FK_BookingItems_Bookings_BookingId",
                        column: x => x.BookingId,
                        principalTable: "Bookings",
                        principalColumn: "BookingId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BookingItems_BookingId",
                table: "BookingItems",
                column: "BookingId");

            migrationBuilder.CreateIndex(
                name: "IX_BookingItems_ShopServiceId",
                table: "BookingItems",
                column: "ShopServiceId");

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_ShopId",
                table: "Bookings",
                column: "ShopId");

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_Status",
                table: "Bookings",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_User_Status",
                table: "Bookings",
                columns: new[] { "UserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_UserId",
                table: "Bookings",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserCartItems_User_Shop_Service",
                table: "UserCartItems",
                columns: new[] { "UserId", "ShopId", "ShopServiceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserCartItems_UserId",
                table: "UserCartItems",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BookingItems");

            migrationBuilder.DropTable(
                name: "UserCartItems");

            migrationBuilder.DropTable(
                name: "Bookings");
        }
    }
}
