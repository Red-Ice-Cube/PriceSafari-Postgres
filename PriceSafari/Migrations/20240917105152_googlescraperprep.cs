using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class googlescraperprep : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PriceData_Products_ProductId",
                table: "PriceData");

            migrationBuilder.DropForeignKey(
                name: "FK_PriceData_Regions_RegionId",
                table: "PriceData");

            migrationBuilder.DropForeignKey(
                name: "FK_PriceData_ScrapeRuns_ScrapeRunId",
                table: "PriceData");

            migrationBuilder.DropIndex(
                name: "IX_PriceData_ProductId",
                table: "PriceData");

            migrationBuilder.RenameColumn(
                name: "ProductId",
                table: "PriceData",
                newName: "ScrapingProductId");

            migrationBuilder.AlterColumn<int>(
                name: "ScrapeRunId",
                table: "PriceData",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<decimal>(
                name: "PriceWithDelivery",
                table: "PriceData",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddForeignKey(
                name: "FK_PriceData_Regions_RegionId",
                table: "PriceData",
                column: "RegionId",
                principalTable: "Regions",
                principalColumn: "RegionId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_PriceData_ScrapeRuns_ScrapeRunId",
                table: "PriceData",
                column: "ScrapeRunId",
                principalTable: "ScrapeRuns",
                principalColumn: "ScrapeRunId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PriceData_Regions_RegionId",
                table: "PriceData");

            migrationBuilder.DropForeignKey(
                name: "FK_PriceData_ScrapeRuns_ScrapeRunId",
                table: "PriceData");

            migrationBuilder.DropColumn(
                name: "PriceWithDelivery",
                table: "PriceData");

            migrationBuilder.RenameColumn(
                name: "ScrapingProductId",
                table: "PriceData",
                newName: "ProductId");

            migrationBuilder.AlterColumn<int>(
                name: "ScrapeRunId",
                table: "PriceData",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PriceData_ProductId",
                table: "PriceData",
                column: "ProductId");

            migrationBuilder.AddForeignKey(
                name: "FK_PriceData_Products_ProductId",
                table: "PriceData",
                column: "ProductId",
                principalTable: "Products",
                principalColumn: "ProductId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_PriceData_Regions_RegionId",
                table: "PriceData",
                column: "RegionId",
                principalTable: "Regions",
                principalColumn: "RegionId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PriceData_ScrapeRuns_ScrapeRunId",
                table: "PriceData",
                column: "ScrapeRunId",
                principalTable: "ScrapeRuns",
                principalColumn: "ScrapeRunId",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
