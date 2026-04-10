using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class colorpilot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "UseColorVariantSearch",
                table: "Stores",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "GoogleColorCode",
                table: "CoOfrs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "UseColorFilter",
                table: "CoOfrs",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UseColorVariantSearch",
                table: "Stores");

            migrationBuilder.DropColumn(
                name: "GoogleColorCode",
                table: "CoOfrs");

            migrationBuilder.DropColumn(
                name: "UseColorFilter",
                table: "CoOfrs");
        }
    }
}
