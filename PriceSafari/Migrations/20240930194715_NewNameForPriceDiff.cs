using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class NewNameForPriceDiff : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "UseProcentageSafari",
                table: "PriceValues",
                newName: "UsePriceDiffSafari");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "UsePriceDiffSafari",
                table: "PriceValues",
                newName: "UseProcentageSafari");
        }
    }
}
