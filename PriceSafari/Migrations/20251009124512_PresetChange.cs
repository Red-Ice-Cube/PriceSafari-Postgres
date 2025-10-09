using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class PresetChange : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsGoogle",
                table: "CompetitorPresetItems");

            migrationBuilder.AddColumn<int>(
                name: "Type",
                table: "CompetitorPresets",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "DataSource",
                table: "CompetitorPresetItems",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Type",
                table: "CompetitorPresets");

            migrationBuilder.DropColumn(
                name: "DataSource",
                table: "CompetitorPresetItems");

            migrationBuilder.AddColumn<bool>(
                name: "IsGoogle",
                table: "CompetitorPresetItems",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }
    }
}
