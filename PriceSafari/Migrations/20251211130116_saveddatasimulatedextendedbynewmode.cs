using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class saveddatasimulatedextendedbynewmode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Mode",
                table: "PriceBridgeItems",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PriceIndexTarget",
                table: "PriceBridgeItems",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "StepPriceApplied",
                table: "PriceBridgeItems",
                type: "decimal(18,2)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Mode",
                table: "PriceBridgeItems");

            migrationBuilder.DropColumn(
                name: "PriceIndexTarget",
                table: "PriceBridgeItems");

            migrationBuilder.DropColumn(
                name: "StepPriceApplied",
                table: "PriceBridgeItems");
        }
    }
}
