using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceTracker.Migrations
{
    /// <inheritdoc />
    public partial class updSQLque : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StoreScrapResults");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StoreScrapResults",
                columns: table => new
                {
                    PriceHistoryCount = table.Column<int>(type: "int", nullable: false),
                    ProductCount = table.Column<int>(type: "int", nullable: false),
                    ScrapCount = table.Column<int>(type: "int", nullable: false),
                    StoreName = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                });
        }
    }
}
