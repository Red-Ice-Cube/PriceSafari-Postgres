using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class ScrappableProducts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ProductsToScrap",
                table: "Stores",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsScrapable",
                table: "Products",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProductsToScrap",
                table: "Stores");

            migrationBuilder.DropColumn(
                name: "IsScrapable",
                table: "Products");
        }
    }
}
