using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class addnewvaluesallegromodeselected : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Mode",
                table: "AllegroPriceBridgeItems",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PriceIndexTarget",
                table: "AllegroPriceBridgeItems",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "StepPriceApplied",
                table: "AllegroPriceBridgeItems",
                type: "decimal(18,2)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Mode",
                table: "AllegroPriceBridgeItems");

            migrationBuilder.DropColumn(
                name: "PriceIndexTarget",
                table: "AllegroPriceBridgeItems");

            migrationBuilder.DropColumn(
                name: "StepPriceApplied",
                table: "AllegroPriceBridgeItems");
        }
    }
}
