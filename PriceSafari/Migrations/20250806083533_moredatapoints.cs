using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class moredatapoints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UseEanForSimulation",
                table: "PriceValues");

            migrationBuilder.AddColumn<string>(
                name: "IdentifierForSimulation",
                table: "PriceValues",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IdentifierForSimulation",
                table: "PriceValues");

            migrationBuilder.AddColumn<bool>(
                name: "UseEanForSimulation",
                table: "PriceValues",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }
    }
}
