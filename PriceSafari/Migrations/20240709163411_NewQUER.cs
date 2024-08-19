using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class NewQUER : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StoreScrapResults",
                columns: table => new
                {
                    StoreName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ProductCount = table.Column<int>(type: "int", nullable: false),
                    ScrapCount = table.Column<int>(type: "int", nullable: false),
                    PriceHistoryCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StoreScrapResults");
        }
    }
}
