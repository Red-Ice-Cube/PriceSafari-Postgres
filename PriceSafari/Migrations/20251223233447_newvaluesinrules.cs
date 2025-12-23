using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class newvaluesinrules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "EnforceMinimalMargin",
                table: "AutomationRules",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsMinimalMarginPercent",
                table: "AutomationRules",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsPriceStepPercent",
                table: "AutomationRules",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "MarketplaceChangePriceForBadgeBestPriceGuarantee",
                table: "AutomationRules",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "MarketplaceChangePriceForBadgeInCampaign",
                table: "AutomationRules",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "MarketplaceChangePriceForBadgeSuperPrice",
                table: "AutomationRules",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "MarketplaceChangePriceForBadgeTopOffer",
                table: "AutomationRules",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "MarketplaceIncludeCommission",
                table: "AutomationRules",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "MinimalMarginValue",
                table: "AutomationRules",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "PriceIndexTargetPercent",
                table: "AutomationRules",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "PriceStep",
                table: "AutomationRules",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "UsePriceWithDelivery",
                table: "AutomationRules",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "UsePurchasePrice",
                table: "AutomationRules",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EnforceMinimalMargin",
                table: "AutomationRules");

            migrationBuilder.DropColumn(
                name: "IsMinimalMarginPercent",
                table: "AutomationRules");

            migrationBuilder.DropColumn(
                name: "IsPriceStepPercent",
                table: "AutomationRules");

            migrationBuilder.DropColumn(
                name: "MarketplaceChangePriceForBadgeBestPriceGuarantee",
                table: "AutomationRules");

            migrationBuilder.DropColumn(
                name: "MarketplaceChangePriceForBadgeInCampaign",
                table: "AutomationRules");

            migrationBuilder.DropColumn(
                name: "MarketplaceChangePriceForBadgeSuperPrice",
                table: "AutomationRules");

            migrationBuilder.DropColumn(
                name: "MarketplaceChangePriceForBadgeTopOffer",
                table: "AutomationRules");

            migrationBuilder.DropColumn(
                name: "MarketplaceIncludeCommission",
                table: "AutomationRules");

            migrationBuilder.DropColumn(
                name: "MinimalMarginValue",
                table: "AutomationRules");

            migrationBuilder.DropColumn(
                name: "PriceIndexTargetPercent",
                table: "AutomationRules");

            migrationBuilder.DropColumn(
                name: "PriceStep",
                table: "AutomationRules");

            migrationBuilder.DropColumn(
                name: "UsePriceWithDelivery",
                table: "AutomationRules");

            migrationBuilder.DropColumn(
                name: "UsePurchasePrice",
                table: "AutomationRules");
        }
    }
}
