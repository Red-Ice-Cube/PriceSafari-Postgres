using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class simple : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
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

            migrationBuilder.AddColumn<int>(
                name: "ScrapHistoryId",
                table: "TableSizeInfo",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StoreId",
                table: "TableSizeInfo",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "UsedSpaceKB",
                table: "TableSizeInfo",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ScrapHistoryId",
                table: "TableSizeInfo");

            migrationBuilder.DropColumn(
                name: "StoreId",
                table: "TableSizeInfo");

            migrationBuilder.DropColumn(
                name: "UsedSpaceKB",
                table: "TableSizeInfo");

            migrationBuilder.AddColumn<decimal>(
                name: "TotalSpaceMB",
                table: "TableSizeInfo",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "UnusedSpaceMB",
                table: "TableSizeInfo",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "UsedSpaceMB",
                table: "TableSizeInfo",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);
        }
    }
}
