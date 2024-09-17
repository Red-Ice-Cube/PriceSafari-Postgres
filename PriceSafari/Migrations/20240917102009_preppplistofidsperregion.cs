using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class preppplistofidsperregion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProductId",
                table: "GoogleScrapingProducts");

            migrationBuilder.AddColumn<string>(
                name: "ProductIds",
                table: "GoogleScrapingProducts",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "[]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProductIds",
                table: "GoogleScrapingProducts");

            migrationBuilder.AddColumn<int>(
                name: "ProductId",
                table: "GoogleScrapingProducts",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }
    }
}
