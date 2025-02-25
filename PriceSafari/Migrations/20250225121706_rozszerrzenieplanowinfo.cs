using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class rozszerrzenieplanowinfo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Ceneo",
                table: "Plans",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "GoogleShopping",
                table: "Plans",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Info",
                table: "Plans",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Ceneo",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "GoogleShopping",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "Info",
                table: "Plans");
        }
    }
}
