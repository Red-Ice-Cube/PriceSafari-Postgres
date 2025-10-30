using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class addedinfosforallegrosettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AllegroEnforceMinimalMargin",
                table: "PriceValues",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "AllegroIdentifierForSimulation",
                table: "PriceValues",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "AllegroMinimalMarginPercent",
                table: "PriceValues",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "AllegroUseMarginForSimulation",
                table: "PriceValues",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AllegroEnforceMinimalMargin",
                table: "PriceValues");

            migrationBuilder.DropColumn(
                name: "AllegroIdentifierForSimulation",
                table: "PriceValues");

            migrationBuilder.DropColumn(
                name: "AllegroMinimalMarginPercent",
                table: "PriceValues");

            migrationBuilder.DropColumn(
                name: "AllegroUseMarginForSimulation",
                table: "PriceValues");
        }
    }
}
