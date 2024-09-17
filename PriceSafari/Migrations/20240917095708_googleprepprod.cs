using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class googleprepprod : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_GoogleScrapingProducts_Products_ProductId",
                table: "GoogleScrapingProducts");

            migrationBuilder.DropForeignKey(
                name: "FK_GoogleScrapingProducts_Regions_RegionId",
                table: "GoogleScrapingProducts");

            migrationBuilder.DropIndex(
                name: "IX_GoogleScrapingProducts_ProductId",
                table: "GoogleScrapingProducts");

            migrationBuilder.DropIndex(
                name: "IX_GoogleScrapingProducts_RegionId",
                table: "GoogleScrapingProducts");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_GoogleScrapingProducts_ProductId",
                table: "GoogleScrapingProducts",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_GoogleScrapingProducts_RegionId",
                table: "GoogleScrapingProducts",
                column: "RegionId");

            migrationBuilder.AddForeignKey(
                name: "FK_GoogleScrapingProducts_Products_ProductId",
                table: "GoogleScrapingProducts",
                column: "ProductId",
                principalTable: "Products",
                principalColumn: "ProductId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_GoogleScrapingProducts_Regions_RegionId",
                table: "GoogleScrapingProducts",
                column: "RegionId",
                principalTable: "Regions",
                principalColumn: "RegionId",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
