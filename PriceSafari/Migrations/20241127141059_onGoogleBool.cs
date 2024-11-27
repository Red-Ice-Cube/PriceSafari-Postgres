using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class onGoogleBool : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GoogleOfferUrl",
                table: "CoOfrPriceHistories");

            migrationBuilder.AddColumn<bool>(
                name: "IsGoogle",
                table: "PriceHistories",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsGoogle",
                table: "PriceHistories");

            migrationBuilder.AddColumn<string>(
                name: "GoogleOfferUrl",
                table: "CoOfrPriceHistories",
                type: "nvarchar(max)",
                nullable: true);
        }
    }
}
