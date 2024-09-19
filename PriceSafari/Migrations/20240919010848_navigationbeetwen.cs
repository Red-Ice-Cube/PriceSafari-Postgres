using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class navigationbeetwen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "GoogleScrapingProductScrapingProductId",
                table: "Products",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Products_GoogleScrapingProductScrapingProductId",
                table: "Products",
                column: "GoogleScrapingProductScrapingProductId");

            migrationBuilder.CreateIndex(
                name: "IX_PriceData_ScrapingProductId",
                table: "PriceData",
                column: "ScrapingProductId");

            migrationBuilder.AddForeignKey(
                name: "FK_PriceData_GoogleScrapingProducts_ScrapingProductId",
                table: "PriceData",
                column: "ScrapingProductId",
                principalTable: "GoogleScrapingProducts",
                principalColumn: "ScrapingProductId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Products_GoogleScrapingProducts_GoogleScrapingProductScrapingProductId",
                table: "Products",
                column: "GoogleScrapingProductScrapingProductId",
                principalTable: "GoogleScrapingProducts",
                principalColumn: "ScrapingProductId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PriceData_GoogleScrapingProducts_ScrapingProductId",
                table: "PriceData");

            migrationBuilder.DropForeignKey(
                name: "FK_Products_GoogleScrapingProducts_GoogleScrapingProductScrapingProductId",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Products_GoogleScrapingProductScrapingProductId",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_PriceData_ScrapingProductId",
                table: "PriceData");

            migrationBuilder.DropColumn(
                name: "GoogleScrapingProductScrapingProductId",
                table: "Products");
        }
    }
}
