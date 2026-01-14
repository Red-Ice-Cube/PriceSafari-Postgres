using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class extendedcataloguescraper : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "UseAdditionalCatalogsForGoogle",
                table: "Stores",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "GoogleCid",
                table: "CoOfrs",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsAdditionalCatalog",
                table: "CoOfrs",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "GoogleCid",
                table: "CoOfrPriceHistories",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UseAdditionalCatalogsForGoogle",
                table: "Stores");

            migrationBuilder.DropColumn(
                name: "GoogleCid",
                table: "CoOfrs");

            migrationBuilder.DropColumn(
                name: "IsAdditionalCatalog",
                table: "CoOfrs");

            migrationBuilder.DropColumn(
                name: "GoogleCid",
                table: "CoOfrPriceHistories");
        }
    }
}
