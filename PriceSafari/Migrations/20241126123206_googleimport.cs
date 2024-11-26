using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class googleimport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GoogleEan",
                table: "ProductMaps",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GoogleExportedName",
                table: "ProductMaps",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GoogleImage",
                table: "ProductMaps",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GoogleEan",
                table: "ProductMaps");

            migrationBuilder.DropColumn(
                name: "GoogleExportedName",
                table: "ProductMaps");

            migrationBuilder.DropColumn(
                name: "GoogleImage",
                table: "ProductMaps");
        }
    }
}
