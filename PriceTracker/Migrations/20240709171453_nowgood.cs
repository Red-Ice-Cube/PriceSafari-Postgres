using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceTracker.Migrations
{
    /// <inheritdoc />
    public partial class nowgood : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RowCounts",
                table: "TableSizeInfo");

            migrationBuilder.DropColumn(
                name: "SchemaName",
                table: "TableSizeInfo");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "RowCounts",
                table: "TableSizeInfo",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "SchemaName",
                table: "TableSizeInfo",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }
    }
}
