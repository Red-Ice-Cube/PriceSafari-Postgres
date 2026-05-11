using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class ProducerView : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsProducer",
                table: "Stores");

            migrationBuilder.AddColumn<decimal>(
                name: "MapPrice",
                table: "Products",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "MapPriceUpdatedDate",
                table: "Products",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AllegroProducerComparisonSource",
                table: "PriceValues",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "AllegroProducerThresholdGreenAmount",
                table: "PriceValues",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "AllegroProducerThresholdGreenDarkAmount",
                table: "PriceValues",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "AllegroProducerThresholdGreenDarkPercent",
                table: "PriceValues",
                type: "numeric(5,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "AllegroProducerThresholdGreenLightAmount",
                table: "PriceValues",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "AllegroProducerThresholdGreenLightPercent",
                table: "PriceValues",
                type: "numeric(5,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "AllegroProducerThresholdGreenPercent",
                table: "PriceValues",
                type: "numeric(5,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "AllegroProducerThresholdRedAmount",
                table: "PriceValues",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "AllegroProducerThresholdRedDarkAmount",
                table: "PriceValues",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "AllegroProducerThresholdRedDarkPercent",
                table: "PriceValues",
                type: "numeric(5,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "AllegroProducerThresholdRedLightAmount",
                table: "PriceValues",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "AllegroProducerThresholdRedLightPercent",
                table: "PriceValues",
                type: "numeric(5,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "AllegroProducerThresholdRedPercent",
                table: "PriceValues",
                type: "numeric(5,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "AllegroProducerUseAmount",
                table: "PriceValues",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "UseProducerViewForMarketplace",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "UseProducerViewForPriceComparison",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "AllegroMapPrice",
                table: "AllegroProducts",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AllegroMapPriceUpdatedDate",
                table: "AllegroProducts",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AllegroMapPriceSnapshot",
                table: "AllegroPriceHistoryExtendedInfos",
                type: "numeric(18,2)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MapPrice",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "MapPriceUpdatedDate",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "AllegroProducerComparisonSource",
                table: "PriceValues");

            migrationBuilder.DropColumn(
                name: "AllegroProducerThresholdGreenAmount",
                table: "PriceValues");

            migrationBuilder.DropColumn(
                name: "AllegroProducerThresholdGreenDarkAmount",
                table: "PriceValues");

            migrationBuilder.DropColumn(
                name: "AllegroProducerThresholdGreenDarkPercent",
                table: "PriceValues");

            migrationBuilder.DropColumn(
                name: "AllegroProducerThresholdGreenLightAmount",
                table: "PriceValues");

            migrationBuilder.DropColumn(
                name: "AllegroProducerThresholdGreenLightPercent",
                table: "PriceValues");

            migrationBuilder.DropColumn(
                name: "AllegroProducerThresholdGreenPercent",
                table: "PriceValues");

            migrationBuilder.DropColumn(
                name: "AllegroProducerThresholdRedAmount",
                table: "PriceValues");

            migrationBuilder.DropColumn(
                name: "AllegroProducerThresholdRedDarkAmount",
                table: "PriceValues");

            migrationBuilder.DropColumn(
                name: "AllegroProducerThresholdRedDarkPercent",
                table: "PriceValues");

            migrationBuilder.DropColumn(
                name: "AllegroProducerThresholdRedLightAmount",
                table: "PriceValues");

            migrationBuilder.DropColumn(
                name: "AllegroProducerThresholdRedLightPercent",
                table: "PriceValues");

            migrationBuilder.DropColumn(
                name: "AllegroProducerThresholdRedPercent",
                table: "PriceValues");

            migrationBuilder.DropColumn(
                name: "AllegroProducerUseAmount",
                table: "PriceValues");

            migrationBuilder.DropColumn(
                name: "UseProducerViewForMarketplace",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "UseProducerViewForPriceComparison",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "AllegroMapPrice",
                table: "AllegroProducts");

            migrationBuilder.DropColumn(
                name: "AllegroMapPriceUpdatedDate",
                table: "AllegroProducts");

            migrationBuilder.DropColumn(
                name: "AllegroMapPriceSnapshot",
                table: "AllegroPriceHistoryExtendedInfos");

            migrationBuilder.AddColumn<bool>(
                name: "IsProducer",
                table: "Stores",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }
    }
}
