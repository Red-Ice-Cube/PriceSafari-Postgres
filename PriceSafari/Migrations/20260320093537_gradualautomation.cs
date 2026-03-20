using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class gradualautomation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "EnableGradualDecrease",
                table: "AutomationRules",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "EnableGradualIncrease",
                table: "AutomationRules",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "GradualDecreaseValue",
                table: "AutomationRules",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "GradualIncreaseValue",
                table: "AutomationRules",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "IsGradualDecreasePercent",
                table: "AutomationRules",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsGradualIncreasePercent",
                table: "AutomationRules",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EnableGradualDecrease",
                table: "AutomationRules");

            migrationBuilder.DropColumn(
                name: "EnableGradualIncrease",
                table: "AutomationRules");

            migrationBuilder.DropColumn(
                name: "GradualDecreaseValue",
                table: "AutomationRules");

            migrationBuilder.DropColumn(
                name: "GradualIncreaseValue",
                table: "AutomationRules");

            migrationBuilder.DropColumn(
                name: "IsGradualDecreasePercent",
                table: "AutomationRules");

            migrationBuilder.DropColumn(
                name: "IsGradualIncreasePercent",
                table: "AutomationRules");
        }
    }
}
