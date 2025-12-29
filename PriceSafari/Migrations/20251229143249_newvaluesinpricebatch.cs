using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class newvaluesinpricebatch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "MaxPriceLimit",
                table: "PriceBridgeItems",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MinPriceLimit",
                table: "PriceBridgeItems",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "WasLimitedByMax",
                table: "PriceBridgeItems",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "WasLimitedByMin",
                table: "PriceBridgeItems",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AutomationRuleId",
                table: "PriceBridgeBatches",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsAutomation",
                table: "PriceBridgeBatches",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "MaxPriceLimit",
                table: "AllegroPriceBridgeItems",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MinPriceLimit",
                table: "AllegroPriceBridgeItems",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "WasLimitedByMax",
                table: "AllegroPriceBridgeItems",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "WasLimitedByMin",
                table: "AllegroPriceBridgeItems",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AutomationRuleId",
                table: "AllegroPriceBridgeBatches",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsAutomation",
                table: "AllegroPriceBridgeBatches",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MaxPriceLimit",
                table: "PriceBridgeItems");

            migrationBuilder.DropColumn(
                name: "MinPriceLimit",
                table: "PriceBridgeItems");

            migrationBuilder.DropColumn(
                name: "WasLimitedByMax",
                table: "PriceBridgeItems");

            migrationBuilder.DropColumn(
                name: "WasLimitedByMin",
                table: "PriceBridgeItems");

            migrationBuilder.DropColumn(
                name: "AutomationRuleId",
                table: "PriceBridgeBatches");

            migrationBuilder.DropColumn(
                name: "IsAutomation",
                table: "PriceBridgeBatches");

            migrationBuilder.DropColumn(
                name: "MaxPriceLimit",
                table: "AllegroPriceBridgeItems");

            migrationBuilder.DropColumn(
                name: "MinPriceLimit",
                table: "AllegroPriceBridgeItems");

            migrationBuilder.DropColumn(
                name: "WasLimitedByMax",
                table: "AllegroPriceBridgeItems");

            migrationBuilder.DropColumn(
                name: "WasLimitedByMin",
                table: "AllegroPriceBridgeItems");

            migrationBuilder.DropColumn(
                name: "AutomationRuleId",
                table: "AllegroPriceBridgeBatches");

            migrationBuilder.DropColumn(
                name: "IsAutomation",
                table: "AllegroPriceBridgeBatches");
        }
    }
}
