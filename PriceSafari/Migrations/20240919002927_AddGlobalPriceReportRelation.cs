using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class AddGlobalPriceReportRelation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_GlobalPriceReports_ProductId",
                table: "GlobalPriceReports",
                column: "ProductId");

            migrationBuilder.AddForeignKey(
                name: "FK_GlobalPriceReports_Products_ProductId",
                table: "GlobalPriceReports",
                column: "ProductId",
                principalTable: "Products",
                principalColumn: "ProductId",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_GlobalPriceReports_Products_ProductId",
                table: "GlobalPriceReports");

            migrationBuilder.DropIndex(
                name: "IX_GlobalPriceReports_ProductId",
                table: "GlobalPriceReports");
        }
    }
}
