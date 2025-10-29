using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class SetCascadeDeleteForAllegroScrapeHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AllegroPriceHistories_AllegroScrapeHistories_AllegroScrapeHistoryId",
                table: "AllegroPriceHistories");

            migrationBuilder.AddForeignKey(
                name: "FK_AllegroPriceHistories_AllegroScrapeHistories_AllegroScrapeHistoryId",
                table: "AllegroPriceHistories",
                column: "AllegroScrapeHistoryId",
                principalTable: "AllegroScrapeHistories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AllegroPriceHistories_AllegroScrapeHistories_AllegroScrapeHistoryId",
                table: "AllegroPriceHistories");

            migrationBuilder.AddForeignKey(
                name: "FK_AllegroPriceHistories_AllegroScrapeHistories_AllegroScrapeHistoryId",
                table: "AllegroPriceHistories",
                column: "AllegroScrapeHistoryId",
                principalTable: "AllegroScrapeHistories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
