using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class productstringlist : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProductIds",
                table: "PriceSafariReports");

            migrationBuilder.AddColumn<string>(
                name: "ProductIdsString",
                table: "PriceSafariReports",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProductIdsString",
                table: "PriceSafariReports");

            migrationBuilder.AddColumn<string>(
                name: "ProductIds",
                table: "PriceSafariReports",
                type: "nvarchar(max)",
                nullable: true);
        }
    }
}
