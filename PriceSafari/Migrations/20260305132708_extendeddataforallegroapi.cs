using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class extendeddataforallegroapi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Producer",
                table: "AllegroProducts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AllegroVisitsCount",
                table: "AllegroPriceHistoryExtendedInfos",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AllegroBrand",
                table: "AllegroOffersToScrape",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AllegroVisitsCount",
                table: "AllegroOffersToScrape",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Producer",
                table: "AllegroProducts");

            migrationBuilder.DropColumn(
                name: "AllegroVisitsCount",
                table: "AllegroPriceHistoryExtendedInfos");

            migrationBuilder.DropColumn(
                name: "AllegroBrand",
                table: "AllegroOffersToScrape");

            migrationBuilder.DropColumn(
                name: "AllegroVisitsCount",
                table: "AllegroOffersToScrape");
        }
    }
}
