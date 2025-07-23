using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class newdatapointssmartsupsell : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Smart",
                table: "AllegroScrapedOffers",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "SuperSeller",
                table: "AllegroScrapedOffers",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "Smart",
                table: "AllegroPriceHistories",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "SuperSeller",
                table: "AllegroPriceHistories",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Smart",
                table: "AllegroScrapedOffers");

            migrationBuilder.DropColumn(
                name: "SuperSeller",
                table: "AllegroScrapedOffers");

            migrationBuilder.DropColumn(
                name: "Smart",
                table: "AllegroPriceHistories");

            migrationBuilder.DropColumn(
                name: "SuperSeller",
                table: "AllegroPriceHistories");
        }
    }
}
