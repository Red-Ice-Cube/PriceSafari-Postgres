using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class addnewvaluesforallegro : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Promoted",
                table: "AllegroScrapedOffers",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "Sponsored",
                table: "AllegroScrapedOffers",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "SuperPrice",
                table: "AllegroScrapedOffers",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "Promoted",
                table: "AllegroPriceHistories",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "Sponsored",
                table: "AllegroPriceHistories",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "SuperPrice",
                table: "AllegroPriceHistories",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Promoted",
                table: "AllegroScrapedOffers");

            migrationBuilder.DropColumn(
                name: "Sponsored",
                table: "AllegroScrapedOffers");

            migrationBuilder.DropColumn(
                name: "SuperPrice",
                table: "AllegroScrapedOffers");

            migrationBuilder.DropColumn(
                name: "Promoted",
                table: "AllegroPriceHistories");

            migrationBuilder.DropColumn(
                name: "Sponsored",
                table: "AllegroPriceHistories");

            migrationBuilder.DropColumn(
                name: "SuperPrice",
                table: "AllegroPriceHistories");
        }
    }
}
