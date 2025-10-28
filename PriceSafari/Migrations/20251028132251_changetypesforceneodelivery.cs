using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class changetypesforceneodelivery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AvailabilityNum",
                table: "PriceHistories");

            migrationBuilder.DropColumn(
                name: "AvailabilityNum",
                table: "CoOfrPriceHistories");

            migrationBuilder.AddColumn<bool>(
                name: "CeneoInStock",
                table: "PriceHistories",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "CeneoInStock",
                table: "CoOfrPriceHistories",
                type: "bit",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CeneoInStock",
                table: "PriceHistories");

            migrationBuilder.DropColumn(
                name: "CeneoInStock",
                table: "CoOfrPriceHistories");

            migrationBuilder.AddColumn<int>(
                name: "AvailabilityNum",
                table: "PriceHistories",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AvailabilityNum",
                table: "CoOfrPriceHistories",
                type: "int",
                nullable: true);
        }
    }
}
