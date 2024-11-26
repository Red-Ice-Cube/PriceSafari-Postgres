using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class newofferclasssetgoogle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ScrapingMethod",
                table: "CoOfrs",
                newName: "GoogleOfferUrl");

            migrationBuilder.AlterColumn<string>(
                name: "OfferUrl",
                table: "CoOfrs",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AddColumn<bool>(
                name: "GoogleIsRejected",
                table: "CoOfrs",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "GoogleIsScraped",
                table: "CoOfrs",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "GooglePricesCount",
                table: "CoOfrs",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GoogleIsRejected",
                table: "CoOfrs");

            migrationBuilder.DropColumn(
                name: "GoogleIsScraped",
                table: "CoOfrs");

            migrationBuilder.DropColumn(
                name: "GooglePricesCount",
                table: "CoOfrs");

            migrationBuilder.RenameColumn(
                name: "GoogleOfferUrl",
                table: "CoOfrs",
                newName: "ScrapingMethod");

            migrationBuilder.AlterColumn<string>(
                name: "OfferUrl",
                table: "CoOfrs",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);
        }
    }
}
