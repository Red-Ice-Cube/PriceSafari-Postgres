using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class intervalpricechanger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IntervalPriceExecutionBatches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    IntervalPriceRuleId = table.Column<int>(type: "integer", nullable: false),
                    StoreId = table.Column<int>(type: "integer", nullable: false),
                    ExecutionDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    EndDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    SlotIndex = table.Column<int>(type: "integer", nullable: false),
                    DayIndex = table.Column<int>(type: "integer", nullable: false),
                    TotalProductsInInterval = table.Column<int>(type: "integer", nullable: false),
                    SuccessCount = table.Column<int>(type: "integer", nullable: false),
                    BlockedCount = table.Column<int>(type: "integer", nullable: false),
                    SkippedCollisionCount = table.Column<int>(type: "integer", nullable: false),
                    FailedCount = table.Column<int>(type: "integer", nullable: false),
                    LimitReachedCount = table.Column<int>(type: "integer", nullable: false),
                    PriceStepApplied = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    IsPriceStepPercent = table.Column<bool>(type: "boolean", nullable: false),
                    Comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    DeviceName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntervalPriceExecutionBatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IntervalPriceExecutionBatches_IntervalPriceRules_IntervalPr~",
                        column: x => x.IntervalPriceRuleId,
                        principalTable: "IntervalPriceRules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IntervalPriceExecutionItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BatchId = table.Column<int>(type: "integer", nullable: false),
                    ProductId = table.Column<int>(type: "integer", nullable: true),
                    AllegroProductId = table.Column<int>(type: "integer", nullable: true),
                    AllegroOfferId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    PriceBefore = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    CommissionBefore = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    PurchasePrice = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    PriceAfterTarget = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    PriceAfterVerified = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    CommissionAfterVerified = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    PriceChange = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    IsInCampaign = table.Column<bool>(type: "boolean", nullable: false),
                    IsSubsidyActive = table.Column<bool>(type: "boolean", nullable: false),
                    CustomerVisiblePrice = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    MinPriceLimit = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    MaxPriceLimit = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    WasLimitedByMin = table.Column<bool>(type: "boolean", nullable: false),
                    WasLimitedByMax = table.Column<bool>(type: "boolean", nullable: false),
                    Success = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    StatusReason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntervalPriceExecutionItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IntervalPriceExecutionItems_IntervalPriceExecutionBatches_B~",
                        column: x => x.BatchId,
                        principalTable: "IntervalPriceExecutionBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IntervalPriceExecutionBatches_IntervalPriceRuleId_Execution~",
                table: "IntervalPriceExecutionBatches",
                columns: new[] { "IntervalPriceRuleId", "ExecutionDate" });

            migrationBuilder.CreateIndex(
                name: "IX_IntervalPriceExecutionBatches_StoreId",
                table: "IntervalPriceExecutionBatches",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_IntervalPriceExecutionItems_AllegroProductId",
                table: "IntervalPriceExecutionItems",
                column: "AllegroProductId");

            migrationBuilder.CreateIndex(
                name: "IX_IntervalPriceExecutionItems_BatchId",
                table: "IntervalPriceExecutionItems",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_IntervalPriceExecutionItems_ProductId",
                table: "IntervalPriceExecutionItems",
                column: "ProductId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IntervalPriceExecutionItems");

            migrationBuilder.DropTable(
                name: "IntervalPriceExecutionBatches");
        }
    }
}
