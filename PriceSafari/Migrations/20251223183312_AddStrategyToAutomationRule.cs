using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class AddStrategyToAutomationRule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CompetitorPresetId",
                table: "AutomationRules",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StrategyMode",
                table: "AutomationRules",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_AutomationRules_CompetitorPresetId",
                table: "AutomationRules",
                column: "CompetitorPresetId");

            migrationBuilder.AddForeignKey(
                name: "FK_AutomationRules_CompetitorPresets_CompetitorPresetId",
                table: "AutomationRules",
                column: "CompetitorPresetId",
                principalTable: "CompetitorPresets",
                principalColumn: "PresetId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AutomationRules_CompetitorPresets_CompetitorPresetId",
                table: "AutomationRules");

            migrationBuilder.DropIndex(
                name: "IX_AutomationRules_CompetitorPresetId",
                table: "AutomationRules");

            migrationBuilder.DropColumn(
                name: "CompetitorPresetId",
                table: "AutomationRules");

            migrationBuilder.DropColumn(
                name: "StrategyMode",
                table: "AutomationRules");
        }
    }
}
