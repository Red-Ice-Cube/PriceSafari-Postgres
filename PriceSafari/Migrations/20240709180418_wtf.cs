using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class wtf : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TableSizeInfo");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TableSizeInfo",
                columns: table => new
                {
                    PriceHistoriesCount = table.Column<int>(type: "int", nullable: false),
                    ScrapHistoryId = table.Column<int>(type: "int", nullable: true),
                    StoreId = table.Column<int>(type: "int", nullable: true),
                    StoreName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TotalSpaceMB = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    UsedSpaceMB = table.Column<decimal>(type: "decimal(18,2)", nullable: false)
                },
                constraints: table =>
                {
                });
        }
    }
}
