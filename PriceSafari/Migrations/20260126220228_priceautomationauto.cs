using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class priceautomationauto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "MarketPlaceAutomationEnabled",
                table: "ScheduleTasks",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "PriceComparisonAutomationEnabled",
                table: "ScheduleTasks",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "MarketPlaceAutomationEnabled",
                table: "DeviceStatuses",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "PriceComparisonAutomationEnabled",
                table: "DeviceStatuses",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MarketPlaceAutomationEnabled",
                table: "ScheduleTasks");

            migrationBuilder.DropColumn(
                name: "PriceComparisonAutomationEnabled",
                table: "ScheduleTasks");

            migrationBuilder.DropColumn(
                name: "MarketPlaceAutomationEnabled",
                table: "DeviceStatuses");

            migrationBuilder.DropColumn(
                name: "PriceComparisonAutomationEnabled",
                table: "DeviceStatuses");
        }
    }
}
