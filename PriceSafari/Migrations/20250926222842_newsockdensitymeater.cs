using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class newsockdensitymeater : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "GoogleInStock",
                table: "PriceHistories",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GoogleOfferPerStoreCount",
                table: "PriceHistories",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GoogleInStock",
                table: "PriceHistories");

            migrationBuilder.DropColumn(
                name: "GoogleOfferPerStoreCount",
                table: "PriceHistories");
        }
    }
}
