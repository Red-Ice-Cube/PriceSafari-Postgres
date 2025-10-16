using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class restricallegrostoredell : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AllegroPriceHistories_AllegroScrapeHistories_AllegroScrapeHistoryId",
                table: "AllegroPriceHistories");

            migrationBuilder.RenameColumn(
                name: "MarginPrice",
                table: "AllegroProducts",
                newName: "AllegroMarginPrice");

            migrationBuilder.AddColumn<string>(
                name: "AllegroApiToken",
                table: "Stores",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "FetchExtendedAllegroData",
                table: "Stores",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsAllegroTokenActive",
                table: "Stores",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "AllegroEan",
                table: "AllegroProducts",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "AllegroOfferId",
                table: "AllegroOffersToScrape",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<bool>(
                name: "AnyPromoActive",
                table: "AllegroOffersToScrape",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ApiAllegroCommission",
                table: "AllegroOffersToScrape",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ApiAllegroPrice",
                table: "AllegroOffersToScrape",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ApiAllegroPriceFromUser",
                table: "AllegroOffersToScrape",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsApiProcessed",
                table: "AllegroOffersToScrape",
                type: "bit",
                nullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_AllegroPriceHistories_AllegroScrapeHistories_AllegroScrapeHistoryId",
                table: "AllegroPriceHistories",
                column: "AllegroScrapeHistoryId",
                principalTable: "AllegroScrapeHistories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AllegroPriceHistories_AllegroScrapeHistories_AllegroScrapeHistoryId",
                table: "AllegroPriceHistories");

            migrationBuilder.DropColumn(
                name: "AllegroApiToken",
                table: "Stores");

            migrationBuilder.DropColumn(
                name: "FetchExtendedAllegroData",
                table: "Stores");

            migrationBuilder.DropColumn(
                name: "IsAllegroTokenActive",
                table: "Stores");

            migrationBuilder.DropColumn(
                name: "AllegroEan",
                table: "AllegroProducts");

            migrationBuilder.DropColumn(
                name: "AllegroOfferId",
                table: "AllegroOffersToScrape");

            migrationBuilder.DropColumn(
                name: "AnyPromoActive",
                table: "AllegroOffersToScrape");

            migrationBuilder.DropColumn(
                name: "ApiAllegroCommission",
                table: "AllegroOffersToScrape");

            migrationBuilder.DropColumn(
                name: "ApiAllegroPrice",
                table: "AllegroOffersToScrape");

            migrationBuilder.DropColumn(
                name: "ApiAllegroPriceFromUser",
                table: "AllegroOffersToScrape");

            migrationBuilder.DropColumn(
                name: "IsApiProcessed",
                table: "AllegroOffersToScrape");

            migrationBuilder.RenameColumn(
                name: "AllegroMarginPrice",
                table: "AllegroProducts",
                newName: "MarginPrice");

            migrationBuilder.AddForeignKey(
                name: "FK_AllegroPriceHistories_AllegroScrapeHistories_AllegroScrapeHistoryId",
                table: "AllegroPriceHistories",
                column: "AllegroScrapeHistoryId",
                principalTable: "AllegroScrapeHistories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
