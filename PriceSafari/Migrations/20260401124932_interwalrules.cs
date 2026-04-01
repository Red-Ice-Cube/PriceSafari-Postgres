using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class interwalrules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IntervalPriceRules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AutomationRuleId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ColorHex = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    PriceStep = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    IsPriceStepPercent = table.Column<bool>(type: "boolean", nullable: false),
                    ScheduleJson = table.Column<string>(type: "text", nullable: false),
                    PreferredBlockSize = table.Column<int>(type: "integer", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    LastExecutionDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    TotalExecutions = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntervalPriceRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IntervalPriceRules_AutomationRules_AutomationRuleId",
                        column: x => x.AutomationRuleId,
                        principalTable: "AutomationRules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IntervalPriceProductAssignments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    IntervalPriceRuleId = table.Column<int>(type: "integer", nullable: false),
                    ProductId = table.Column<int>(type: "integer", nullable: true),
                    AllegroProductId = table.Column<int>(type: "integer", nullable: true),
                    AssignedDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntervalPriceProductAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IntervalPriceProductAssignments_AllegroProducts_AllegroProd~",
                        column: x => x.AllegroProductId,
                        principalTable: "AllegroProducts",
                        principalColumn: "AllegroProductId");
                    table.ForeignKey(
                        name: "FK_IntervalPriceProductAssignments_IntervalPriceRules_Interval~",
                        column: x => x.IntervalPriceRuleId,
                        principalTable: "IntervalPriceRules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_IntervalPriceProductAssignments_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "ProductId");
                });

            migrationBuilder.CreateIndex(
                name: "IX_IntervalPriceProductAssignments_AllegroProductId",
                table: "IntervalPriceProductAssignments",
                column: "AllegroProductId",
                unique: true,
                filter: "\"AllegroProductId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_IntervalPriceProductAssignments_IntervalPriceRuleId",
                table: "IntervalPriceProductAssignments",
                column: "IntervalPriceRuleId");

            migrationBuilder.CreateIndex(
                name: "IX_IntervalPriceProductAssignments_ProductId",
                table: "IntervalPriceProductAssignments",
                column: "ProductId",
                unique: true,
                filter: "\"ProductId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_IntervalPriceRules_AutomationRuleId",
                table: "IntervalPriceRules",
                column: "AutomationRuleId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IntervalPriceProductAssignments");

            migrationBuilder.DropTable(
                name: "IntervalPriceRules");
        }
    }
}
