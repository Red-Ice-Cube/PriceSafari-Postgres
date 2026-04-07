using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class abcrecordsforinterwal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsPriceStepPercentB",
                table: "IntervalPriceRules",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsPriceStepPercentC",
                table: "IntervalPriceRules",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsStepAActive",
                table: "IntervalPriceRules",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsStepBActive",
                table: "IntervalPriceRules",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsStepCActive",
                table: "IntervalPriceRules",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "PriceStepB",
                table: "IntervalPriceRules",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "PriceStepC",
                table: "IntervalPriceRules",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsPriceStepPercentB",
                table: "IntervalPriceRules");

            migrationBuilder.DropColumn(
                name: "IsPriceStepPercentC",
                table: "IntervalPriceRules");

            migrationBuilder.DropColumn(
                name: "IsStepAActive",
                table: "IntervalPriceRules");

            migrationBuilder.DropColumn(
                name: "IsStepBActive",
                table: "IntervalPriceRules");

            migrationBuilder.DropColumn(
                name: "IsStepCActive",
                table: "IntervalPriceRules");

            migrationBuilder.DropColumn(
                name: "PriceStepB",
                table: "IntervalPriceRules");

            migrationBuilder.DropColumn(
                name: "PriceStepC",
                table: "IntervalPriceRules");
        }
    }
}
