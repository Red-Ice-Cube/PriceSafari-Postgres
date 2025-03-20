using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class othersimulationrules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "EnforceMinimalMargin",
                table: "PriceValues",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MinimalMarginPercent",
                table: "PriceValues",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "UseMarginForSimulation",
                table: "PriceValues",
                type: "bit",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EnforceMinimalMargin",
                table: "PriceValues");

            migrationBuilder.DropColumn(
                name: "MinimalMarginPercent",
                table: "PriceValues");

            migrationBuilder.DropColumn(
                name: "UseMarginForSimulation",
                table: "PriceValues");
        }
    }
}
