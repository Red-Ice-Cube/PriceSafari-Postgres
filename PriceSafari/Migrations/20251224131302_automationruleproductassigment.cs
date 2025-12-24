using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class automationruleproductassigment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AutomationProductAssignments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AutomationRuleId = table.Column<int>(type: "int", nullable: false),
                    ProductId = table.Column<int>(type: "int", nullable: true),
                    AllegroProductId = table.Column<int>(type: "int", nullable: true),
                    AssignedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AutomationProductAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AutomationProductAssignments_AllegroProducts_AllegroProductId",
                        column: x => x.AllegroProductId,
                        principalTable: "AllegroProducts",
                        principalColumn: "AllegroProductId");
                    table.ForeignKey(
                        name: "FK_AutomationProductAssignments_AutomationRules_AutomationRuleId",
                        column: x => x.AutomationRuleId,
                        principalTable: "AutomationRules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AutomationProductAssignments_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "ProductId");
                });

            migrationBuilder.CreateIndex(
                name: "IX_AutomationProductAssignments_AllegroProductId",
                table: "AutomationProductAssignments",
                column: "AllegroProductId",
                unique: true,
                filter: "[AllegroProductId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AutomationProductAssignments_AutomationRuleId",
                table: "AutomationProductAssignments",
                column: "AutomationRuleId");

            migrationBuilder.CreateIndex(
                name: "IX_AutomationProductAssignments_ProductId",
                table: "AutomationProductAssignments",
                column: "ProductId",
                unique: true,
                filter: "[ProductId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AutomationProductAssignments");
        }
    }
}
