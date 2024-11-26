using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class stayname : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Ean",
                table: "Products",
                newName: "Ean");

            migrationBuilder.RenameColumn(
                name: "Ean",
                table: "ProductMaps",
                newName: "Ean");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Ean",
                table: "Products",
                newName: "Ean");

            migrationBuilder.RenameColumn(
                name: "Ean",
                table: "ProductMaps",
                newName: "Ean");
        }
    }
}
