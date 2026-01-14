using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class removedurlfromextendedprod : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GoogleUrl",
                table: "ProductGoogleCatalogs");

            migrationBuilder.AlterColumn<string>(
                name: "GoogleGid",
                table: "ProductGoogleCatalogs",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AddColumn<bool>(
                name: "IsExtendedOfferByHid",
                table: "ProductGoogleCatalogs",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsExtendedOfferByHid",
                table: "ProductGoogleCatalogs");

            migrationBuilder.AlterColumn<string>(
                name: "GoogleGid",
                table: "ProductGoogleCatalogs",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GoogleUrl",
                table: "ProductGoogleCatalogs",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }
    }
}
