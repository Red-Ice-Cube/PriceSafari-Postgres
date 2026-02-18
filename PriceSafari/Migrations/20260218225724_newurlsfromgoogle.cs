using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class newurlsfromgoogle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "GoogleCid",
                table: "CoOfrPriceHistories",
                newName: "GoogleOfferUrl");

            migrationBuilder.AddColumn<bool>(
                name: "CollectGoogleStoreLinks",
                table: "Stores",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GoogleOfferUrl",
                table: "PriceHistories",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "CollectGoogleStoreLinks",
                table: "CoOfrs",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CollectGoogleStoreLinks",
                table: "Stores");

            migrationBuilder.DropColumn(
                name: "GoogleOfferUrl",
                table: "PriceHistories");

            migrationBuilder.DropColumn(
                name: "CollectGoogleStoreLinks",
                table: "CoOfrs");

            migrationBuilder.RenameColumn(
                name: "GoogleOfferUrl",
                table: "CoOfrPriceHistories",
                newName: "GoogleCid");
        }
    }
}
