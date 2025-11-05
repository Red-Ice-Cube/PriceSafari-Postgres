using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class allegropricechangsave : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AllegroPriceBridgeBatches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ExecutionDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    StoreId = table.Column<int>(type: "int", nullable: false),
                    AllegroScrapeHistoryId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    SuccessfulCount = table.Column<int>(type: "int", nullable: false),
                    FailedCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AllegroPriceBridgeBatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AllegroPriceBridgeBatches_AllegroScrapeHistories_AllegroScrapeHistoryId",
                        column: x => x.AllegroScrapeHistoryId,
                        principalTable: "AllegroScrapeHistories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AllegroPriceBridgeBatches_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_AllegroPriceBridgeBatches_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "StoreId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AllegroPriceBridgeItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AllegroPriceBridgeBatchId = table.Column<int>(type: "int", nullable: false),
                    AllegroProductId = table.Column<int>(type: "int", nullable: false),
                    AllegroOfferId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Success = table.Column<bool>(type: "bit", nullable: false),
                    ErrorMessage = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PriceBefore = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CommissionBefore = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    MarginAmountBefore = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    MarginPercentBefore = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    RankingBefore = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PriceAfter_Simulated = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CommissionAfter_Simulated = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    MarginAmountAfter_Simulated = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    MarginPercentAfter_Simulated = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    RankingAfter_Simulated = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PriceAfter_Verified = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    CommissionAfter_Verified = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    MarginAmountAfter_Verified = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    MarginPercentAfter_Verified = table.Column<decimal>(type: "decimal(18,2)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AllegroPriceBridgeItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AllegroPriceBridgeItems_AllegroPriceBridgeBatches_AllegroPriceBridgeBatchId",
                        column: x => x.AllegroPriceBridgeBatchId,
                        principalTable: "AllegroPriceBridgeBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AllegroPriceBridgeItems_AllegroProducts_AllegroProductId",
                        column: x => x.AllegroProductId,
                        principalTable: "AllegroProducts",
                        principalColumn: "AllegroProductId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AllegroPriceBridgeBatches_AllegroScrapeHistoryId",
                table: "AllegroPriceBridgeBatches",
                column: "AllegroScrapeHistoryId");

            migrationBuilder.CreateIndex(
                name: "IX_AllegroPriceBridgeBatches_StoreId",
                table: "AllegroPriceBridgeBatches",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_AllegroPriceBridgeBatches_UserId",
                table: "AllegroPriceBridgeBatches",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AllegroPriceBridgeItems_AllegroPriceBridgeBatchId",
                table: "AllegroPriceBridgeItems",
                column: "AllegroPriceBridgeBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_AllegroPriceBridgeItems_AllegroProductId",
                table: "AllegroPriceBridgeItems",
                column: "AllegroProductId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AllegroPriceBridgeItems");

            migrationBuilder.DropTable(
                name: "AllegroPriceBridgeBatches");
        }
    }
}
