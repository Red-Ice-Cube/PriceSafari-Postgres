using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class testscca : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_PriceBridgeBatches_AutomationRuleId",
                table: "PriceBridgeBatches",
                column: "AutomationRuleId");

            migrationBuilder.CreateIndex(
                name: "IX_AllegroPriceBridgeBatches_AutomationRuleId",
                table: "AllegroPriceBridgeBatches",
                column: "AutomationRuleId");

            migrationBuilder.AddForeignKey(
                name: "FK_AllegroPriceBridgeBatches_AutomationRules_AutomationRuleId",
                table: "AllegroPriceBridgeBatches",
                column: "AutomationRuleId",
                principalTable: "AutomationRules",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_PriceBridgeBatches_AutomationRules_AutomationRuleId",
                table: "PriceBridgeBatches",
                column: "AutomationRuleId",
                principalTable: "AutomationRules",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AllegroPriceBridgeBatches_AutomationRules_AutomationRuleId",
                table: "AllegroPriceBridgeBatches");

            migrationBuilder.DropForeignKey(
                name: "FK_PriceBridgeBatches_AutomationRules_AutomationRuleId",
                table: "PriceBridgeBatches");

            migrationBuilder.DropIndex(
                name: "IX_PriceBridgeBatches_AutomationRuleId",
                table: "PriceBridgeBatches");

            migrationBuilder.DropIndex(
                name: "IX_AllegroPriceBridgeBatches_AutomationRuleId",
                table: "AllegroPriceBridgeBatches");
        }
    }
}
