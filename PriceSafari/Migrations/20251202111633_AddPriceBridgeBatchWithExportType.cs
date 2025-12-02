using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class AddPriceBridgeBatchWithExportType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PriceBridgeBatches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StoreId = table.Column<int>(type: "int", nullable: false),
                    ScrapHistoryId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    ExecutionDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SuccessfulCount = table.Column<int>(type: "int", nullable: false),
                    ExportMethod = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PriceBridgeBatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PriceBridgeBatches_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_PriceBridgeBatches_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "StoreId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PriceBridgeItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PriceBridgeBatchId = table.Column<int>(type: "int", nullable: false),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    PriceBefore = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    PriceAfter = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    MarginPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    RankingGoogleBefore = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RankingCeneoBefore = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RankingGoogleAfterSimulated = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RankingCeneoAfterSimulated = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Success = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PriceBridgeItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PriceBridgeItems_PriceBridgeBatches_PriceBridgeBatchId",
                        column: x => x.PriceBridgeBatchId,
                        principalTable: "PriceBridgeBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PriceBridgeItems_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "ProductId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PriceBridgeBatches_StoreId",
                table: "PriceBridgeBatches",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_PriceBridgeBatches_UserId",
                table: "PriceBridgeBatches",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_PriceBridgeItems_PriceBridgeBatchId",
                table: "PriceBridgeItems",
                column: "PriceBridgeBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_PriceBridgeItems_ProductId",
                table: "PriceBridgeItems",
                column: "ProductId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PriceBridgeItems");

            migrationBuilder.DropTable(
                name: "PriceBridgeBatches");
        }
    }
}
