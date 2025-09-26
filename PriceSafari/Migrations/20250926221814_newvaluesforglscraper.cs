using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class newvaluesforglscraper : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "GoogleInStock",
                table: "CoOfrPriceHistories",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GoogleOfferPerStoreCount",
                table: "CoOfrPriceHistories",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GoogleInStock",
                table: "CoOfrPriceHistories");

            migrationBuilder.DropColumn(
                name: "GoogleOfferPerStoreCount",
                table: "CoOfrPriceHistories");
        }
    }
}
