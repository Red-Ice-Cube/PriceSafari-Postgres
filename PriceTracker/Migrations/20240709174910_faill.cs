using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceTracker.Migrations
{
    /// <inheritdoc />
    public partial class faill : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "UsedSpaceMB",
                table: "TableSizeInfo",
                type: "decimal(18,2)",
                nullable: false,
                oldClrType: typeof(double),
                oldType: "float");

            migrationBuilder.AddColumn<decimal>(
                name: "TotalSpaceMB",
                table: "TableSizeInfo",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TotalSpaceMB",
                table: "TableSizeInfo");

            migrationBuilder.AlterColumn<double>(
                name: "UsedSpaceMB",
                table: "TableSizeInfo",
                type: "float",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");
        }
    }
}
