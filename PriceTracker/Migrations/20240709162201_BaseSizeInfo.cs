using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceTracker.Migrations
{
    /// <inheritdoc />
    public partial class BaseSizeInfo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UsedSpaceKB",
                table: "TableSizeInfo");

            migrationBuilder.AddColumn<double>(
                name: "TotalSpaceMB",
                table: "TableSizeInfo",
                type: "float",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "UnusedSpaceMB",
                table: "TableSizeInfo",
                type: "float",
                nullable: false,
                defaultValue: 0.0);

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
                name: "TotalSpaceMB",
                table: "TableSizeInfo");

            migrationBuilder.DropColumn(
                name: "UnusedSpaceMB",
                table: "TableSizeInfo");

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
