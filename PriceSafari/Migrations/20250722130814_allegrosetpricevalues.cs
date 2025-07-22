using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class allegrosetpricevalues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AllegroPriceStep",
                table: "PriceValues",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "AllegroSetPrice1",
                table: "PriceValues",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "AllegroSetPrice2",
                table: "PriceValues",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AllegroPriceStep",
                table: "PriceValues");

            migrationBuilder.DropColumn(
                name: "AllegroSetPrice1",
                table: "PriceValues");

            migrationBuilder.DropColumn(
                name: "AllegroSetPrice2",
                table: "PriceValues");
        }
    }
}
