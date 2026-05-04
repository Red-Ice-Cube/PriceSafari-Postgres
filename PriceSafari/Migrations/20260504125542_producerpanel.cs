using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class producerpanel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ProducerComparisonSource",
                table: "PriceValues",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "ProducerThresholdGreenAmount",
                table: "PriceValues",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ProducerThresholdGreenDarkAmount",
                table: "PriceValues",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ProducerThresholdGreenDarkPercent",
                table: "PriceValues",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ProducerThresholdGreenLightAmount",
                table: "PriceValues",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ProducerThresholdGreenLightPercent",
                table: "PriceValues",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ProducerThresholdGreenPercent",
                table: "PriceValues",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ProducerThresholdRedAmount",
                table: "PriceValues",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ProducerThresholdRedDarkAmount",
                table: "PriceValues",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ProducerThresholdRedDarkPercent",
                table: "PriceValues",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ProducerThresholdRedLightAmount",
                table: "PriceValues",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ProducerThresholdRedLightPercent",
                table: "PriceValues",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ProducerThresholdRedPercent",
                table: "PriceValues",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "ProducerUseAmount",
                table: "PriceValues",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProducerComparisonSource",
                table: "PriceValues");

            migrationBuilder.DropColumn(
                name: "ProducerThresholdGreenAmount",
                table: "PriceValues");

            migrationBuilder.DropColumn(
                name: "ProducerThresholdGreenDarkAmount",
                table: "PriceValues");

            migrationBuilder.DropColumn(
                name: "ProducerThresholdGreenDarkPercent",
                table: "PriceValues");

            migrationBuilder.DropColumn(
                name: "ProducerThresholdGreenLightAmount",
                table: "PriceValues");

            migrationBuilder.DropColumn(
                name: "ProducerThresholdGreenLightPercent",
                table: "PriceValues");

            migrationBuilder.DropColumn(
                name: "ProducerThresholdGreenPercent",
                table: "PriceValues");

            migrationBuilder.DropColumn(
                name: "ProducerThresholdRedAmount",
                table: "PriceValues");

            migrationBuilder.DropColumn(
                name: "ProducerThresholdRedDarkAmount",
                table: "PriceValues");

            migrationBuilder.DropColumn(
                name: "ProducerThresholdRedDarkPercent",
                table: "PriceValues");

            migrationBuilder.DropColumn(
                name: "ProducerThresholdRedLightAmount",
                table: "PriceValues");

            migrationBuilder.DropColumn(
                name: "ProducerThresholdRedLightPercent",
                table: "PriceValues");

            migrationBuilder.DropColumn(
                name: "ProducerThresholdRedPercent",
                table: "PriceValues");

            migrationBuilder.DropColumn(
                name: "ProducerUseAmount",
                table: "PriceValues");
        }
    }
}
