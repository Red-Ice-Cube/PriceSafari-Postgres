using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class newsettingsforallegropricesim : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AllegroChangePriceForBagdeBestPriceGuarantee",
                table: "PriceValues",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AllegroChangePriceForBagdeInCampaign",
                table: "PriceValues",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AllegroChangePriceForBagdeSuperPrice",
                table: "PriceValues",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AllegroChangePriceForBagdeTopOffer",
                table: "PriceValues",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AllegroChangePriceForBagdeBestPriceGuarantee",
                table: "PriceValues");

            migrationBuilder.DropColumn(
                name: "AllegroChangePriceForBagdeInCampaign",
                table: "PriceValues");

            migrationBuilder.DropColumn(
                name: "AllegroChangePriceForBagdeSuperPrice",
                table: "PriceValues");

            migrationBuilder.DropColumn(
                name: "AllegroChangePriceForBagdeTopOffer",
                table: "PriceValues");
        }
    }
}
