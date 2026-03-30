using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class extravaluesforpricehistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "GooglePackUnits",
                table: "PriceHistories",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "GooglePricePerKg",
                table: "PriceHistories",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "GoogleUnitWeightG",
                table: "PriceHistories",
                type: "numeric",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GooglePackUnits",
                table: "PriceHistories");

            migrationBuilder.DropColumn(
                name: "GooglePricePerKg",
                table: "PriceHistories");

            migrationBuilder.DropColumn(
                name: "GoogleUnitWeightG",
                table: "PriceHistories");
        }
    }
}
