using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class scrapertitleextend : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GoogleCountryCode",
                table: "Stores",
                type: "character varying(2)",
                maxLength: 2,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "GoogleGetTitle",
                table: "Stores",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "UseCalculationEnginePerKG",
                table: "Stores",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "GoogleCountryCode",
                table: "CoOfrs",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "GoogleGetTitle",
                table: "CoOfrs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "GoogleOfferTitle",
                table: "CoOfrPriceHistories",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GoogleCountryCode",
                table: "Stores");

            migrationBuilder.DropColumn(
                name: "GoogleGetTitle",
                table: "Stores");

            migrationBuilder.DropColumn(
                name: "UseCalculationEnginePerKG",
                table: "Stores");

            migrationBuilder.DropColumn(
                name: "GoogleCountryCode",
                table: "CoOfrs");

            migrationBuilder.DropColumn(
                name: "GoogleGetTitle",
                table: "CoOfrs");

            migrationBuilder.DropColumn(
                name: "GoogleOfferTitle",
                table: "CoOfrPriceHistories");
        }
    }
}
