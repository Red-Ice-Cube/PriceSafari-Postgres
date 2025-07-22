using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class allegroofferrnscryptlogic : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AllegroScrapeHistories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    StoreId = table.Column<int>(type: "int", nullable: false),
                    ProcessedUrlsCount = table.Column<int>(type: "int", nullable: false),
                    SavedOffersCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AllegroScrapeHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AllegroScrapeHistories_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "StoreId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AllegroPriceHistories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AllegroProductId = table.Column<int>(type: "int", nullable: false),
                    AllegroScrapeHistoryId = table.Column<int>(type: "int", nullable: false),
                    SellerName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Price = table.Column<decimal>(type: "decimal(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AllegroPriceHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AllegroPriceHistories_AllegroProducts_AllegroProductId",
                        column: x => x.AllegroProductId,
                        principalTable: "AllegroProducts",
                        principalColumn: "AllegroProductId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AllegroPriceHistories_AllegroScrapeHistories_AllegroScrapeHistoryId",
                        column: x => x.AllegroScrapeHistoryId,
                        principalTable: "AllegroScrapeHistories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AllegroPriceHistories_AllegroProductId",
                table: "AllegroPriceHistories",
                column: "AllegroProductId");

            migrationBuilder.CreateIndex(
                name: "IX_AllegroPriceHistories_AllegroScrapeHistoryId",
                table: "AllegroPriceHistories",
                column: "AllegroScrapeHistoryId");

            migrationBuilder.CreateIndex(
                name: "IX_AllegroScrapeHistories_StoreId",
                table: "AllegroScrapeHistories",
                column: "StoreId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AllegroPriceHistories");

            migrationBuilder.DropTable(
                name: "AllegroScrapeHistories");
        }
    }
}
