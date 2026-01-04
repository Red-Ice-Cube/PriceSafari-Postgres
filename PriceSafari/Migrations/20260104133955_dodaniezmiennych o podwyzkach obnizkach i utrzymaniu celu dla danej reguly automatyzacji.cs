using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class dodaniezmiennychopodwyzkachobnizkachiutrzymaniuceludladanejregulyautomatyzacji : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PriceDecreasedCount",
                table: "PriceBridgeBatches",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PriceIncreasedCount",
                table: "PriceBridgeBatches",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PriceMaintainedCount",
                table: "PriceBridgeBatches",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PriceDecreasedCount",
                table: "AllegroPriceBridgeBatches",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PriceIncreasedCount",
                table: "AllegroPriceBridgeBatches",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PriceMaintainedCount",
                table: "AllegroPriceBridgeBatches",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PriceDecreasedCount",
                table: "PriceBridgeBatches");

            migrationBuilder.DropColumn(
                name: "PriceIncreasedCount",
                table: "PriceBridgeBatches");

            migrationBuilder.DropColumn(
                name: "PriceMaintainedCount",
                table: "PriceBridgeBatches");

            migrationBuilder.DropColumn(
                name: "PriceDecreasedCount",
                table: "AllegroPriceBridgeBatches");

            migrationBuilder.DropColumn(
                name: "PriceIncreasedCount",
                table: "AllegroPriceBridgeBatches");

            migrationBuilder.DropColumn(
                name: "PriceMaintainedCount",
                table: "AllegroPriceBridgeBatches");
        }
    }
}
