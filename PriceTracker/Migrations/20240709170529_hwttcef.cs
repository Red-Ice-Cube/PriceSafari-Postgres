using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceTracker.Migrations
{
    /// <inheritdoc />
    public partial class hwttcef : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "StoreName",
                table: "TableSizeInfo",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StoreName",
                table: "TableSizeInfo");
        }
    }
}
