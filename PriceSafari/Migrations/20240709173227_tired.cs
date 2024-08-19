using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class tired : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UsedSpaceKB",
                table: "TableSizeInfo");

            migrationBuilder.AddColumn<double>(
                name: "UsedSpaceMB",
                table: "TableSizeInfo",
                type: "float",
                nullable: false,
                defaultValue: 0.0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UsedSpaceMB",
                table: "TableSizeInfo");

            migrationBuilder.AddColumn<long>(
                name: "UsedSpaceKB",
                table: "TableSizeInfo",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }
    }
}
