using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class additionalinfoclass : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PriceHistoryExtendedInfos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    ScrapHistoryId = table.Column<int>(type: "int", nullable: false),
                    CeneoSalesCount = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PriceHistoryExtendedInfos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PriceHistoryExtendedInfos_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "ProductId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PriceHistoryExtendedInfos_ScrapHistories_ScrapHistoryId",
                        column: x => x.ScrapHistoryId,
                        principalTable: "ScrapHistories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PriceHistoryExtendedInfos_ProductId_ScrapHistoryId",
                table: "PriceHistoryExtendedInfos",
                columns: new[] { "ProductId", "ScrapHistoryId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PriceHistoryExtendedInfos_ScrapHistoryId",
                table: "PriceHistoryExtendedInfos",
                column: "ScrapHistoryId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PriceHistoryExtendedInfos");
        }
    }
}
