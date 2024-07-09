using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceTracker.Migrations
{
    /// <inheritdoc />
    public partial class whtnw : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PriceHistoriesCount",
                table: "TableSizeInfo",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PriceHistoriesCount",
                table: "TableSizeInfo");
        }
    }
}
