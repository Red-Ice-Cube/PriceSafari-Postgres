using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class newitemsforallegro : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "DeliveryCost",
                table: "AllegroScrapedOffers",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeliveryTime",
                table: "AllegroScrapedOffers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Popularity",
                table: "AllegroScrapedOffers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DeliveryCost",
                table: "AllegroPriceHistories",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeliveryTime",
                table: "AllegroPriceHistories",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Popularity",
                table: "AllegroPriceHistories",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeliveryCost",
                table: "AllegroScrapedOffers");

            migrationBuilder.DropColumn(
                name: "DeliveryTime",
                table: "AllegroScrapedOffers");

            migrationBuilder.DropColumn(
                name: "Popularity",
                table: "AllegroScrapedOffers");

            migrationBuilder.DropColumn(
                name: "DeliveryCost",
                table: "AllegroPriceHistories");

            migrationBuilder.DropColumn(
                name: "DeliveryTime",
                table: "AllegroPriceHistories");

            migrationBuilder.DropColumn(
                name: "Popularity",
                table: "AllegroPriceHistories");
        }
    }
}
