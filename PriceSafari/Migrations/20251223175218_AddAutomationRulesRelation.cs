using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class AddAutomationRulesRelation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "StoreClassStoreId",
                table: "AutomationRules",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AutomationRules_StoreClassStoreId",
                table: "AutomationRules",
                column: "StoreClassStoreId");

            migrationBuilder.AddForeignKey(
                name: "FK_AutomationRules_Stores_StoreClassStoreId",
                table: "AutomationRules",
                column: "StoreClassStoreId",
                principalTable: "Stores",
                principalColumn: "StoreId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AutomationRules_Stores_StoreClassStoreId",
                table: "AutomationRules");

            migrationBuilder.DropIndex(
                name: "IX_AutomationRules_StoreClassStoreId",
                table: "AutomationRules");

            migrationBuilder.DropColumn(
                name: "StoreClassStoreId",
                table: "AutomationRules");
        }
    }
}
