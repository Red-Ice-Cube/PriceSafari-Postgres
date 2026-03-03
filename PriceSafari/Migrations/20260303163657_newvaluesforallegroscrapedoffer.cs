using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class newvaluesforallegroscrapedoffer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RatingCount",
                table: "AllegroScrapedOffers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "RatingPositivePercent",
                table: "AllegroScrapedOffers",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "StoreIdOnAllegro",
                table: "AllegroScrapedOffers",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RatingCount",
                table: "AllegroPriceHistories",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "RatingPositivePercent",
                table: "AllegroPriceHistories",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "StoreIdOnAllegro",
                table: "AllegroPriceHistories",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RatingCount",
                table: "AllegroScrapedOffers");

            migrationBuilder.DropColumn(
                name: "RatingPositivePercent",
                table: "AllegroScrapedOffers");

            migrationBuilder.DropColumn(
                name: "StoreIdOnAllegro",
                table: "AllegroScrapedOffers");

            migrationBuilder.DropColumn(
                name: "RatingCount",
                table: "AllegroPriceHistories");

            migrationBuilder.DropColumn(
                name: "RatingPositivePercent",
                table: "AllegroPriceHistories");

            migrationBuilder.DropColumn(
                name: "StoreIdOnAllegro",
                table: "AllegroPriceHistories");
        }
    }
}
