using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class ProcDiffChane : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "PercentageDifferenceFromSetPrice1",
                table: "PriceValues",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "PercentageDifferenceFromSetPrice2",
                table: "PriceValues",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PercentageDifferenceFromSetPrice1",
                table: "PriceValues");

            migrationBuilder.DropColumn(
                name: "PercentageDifferenceFromSetPrice2",
                table: "PriceValues");
        }
    }
}
