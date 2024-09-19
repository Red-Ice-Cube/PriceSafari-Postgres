using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class CurrencyMultiply : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CurrencyValue",
                table: "Regions",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "CalculatedPrice",
                table: "GlobalPriceReports",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "CalculatedPriceWithDelivery",
                table: "GlobalPriceReports",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CurrencyValue",
                table: "Regions");

            migrationBuilder.DropColumn(
                name: "CalculatedPrice",
                table: "GlobalPriceReports");

            migrationBuilder.DropColumn(
                name: "CalculatedPriceWithDelivery",
                table: "GlobalPriceReports");
        }
    }
}
