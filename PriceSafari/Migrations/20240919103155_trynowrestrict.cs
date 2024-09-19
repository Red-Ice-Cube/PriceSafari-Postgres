using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class trynowrestrict : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_GoogleScrapingProducts_RegionId",
                table: "GoogleScrapingProducts",
                column: "RegionId");

            migrationBuilder.CreateIndex(
                name: "IX_GlobalPriceReports_RegionId",
                table: "GlobalPriceReports",
                column: "RegionId");

            migrationBuilder.AddForeignKey(
                name: "FK_GlobalPriceReports_Regions_RegionId",
                table: "GlobalPriceReports",
                column: "RegionId",
                principalTable: "Regions",
                principalColumn: "RegionId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_GoogleScrapingProducts_Regions_RegionId",
                table: "GoogleScrapingProducts",
                column: "RegionId",
                principalTable: "Regions",
                principalColumn: "RegionId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_GlobalPriceReports_Regions_RegionId",
                table: "GlobalPriceReports");

            migrationBuilder.DropForeignKey(
                name: "FK_GoogleScrapingProducts_Regions_RegionId",
                table: "GoogleScrapingProducts");

            migrationBuilder.DropIndex(
                name: "IX_GoogleScrapingProducts_RegionId",
                table: "GoogleScrapingProducts");

            migrationBuilder.DropIndex(
                name: "IX_GlobalPriceReports_RegionId",
                table: "GlobalPriceReports");
        }
    }
}
