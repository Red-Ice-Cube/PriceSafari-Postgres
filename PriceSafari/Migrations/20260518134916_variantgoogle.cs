using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class variantgoogle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "UseColorVariantSearch",
                table: "Stores",
                newName: "UseVariantSearch");

            migrationBuilder.RenameColumn(
                name: "GoogleColorCode",
                table: "Products",
                newName: "GoogleVariantCode");

            migrationBuilder.RenameColumn(
                name: "GoogleColor",
                table: "Products",
                newName: "GoogleVariant");

            migrationBuilder.RenameColumn(
                name: "UseColorFilter",
                table: "CoOfrs",
                newName: "UseVariantFilter");

            migrationBuilder.RenameColumn(
                name: "GoogleColorCode",
                table: "CoOfrs",
                newName: "GoogleVariantCode");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "UseVariantSearch",
                table: "Stores",
                newName: "UseColorVariantSearch");

            migrationBuilder.RenameColumn(
                name: "GoogleVariantCode",
                table: "Products",
                newName: "GoogleColorCode");

            migrationBuilder.RenameColumn(
                name: "GoogleVariant",
                table: "Products",
                newName: "GoogleColor");

            migrationBuilder.RenameColumn(
                name: "UseVariantFilter",
                table: "CoOfrs",
                newName: "UseColorFilter");

            migrationBuilder.RenameColumn(
                name: "GoogleVariantCode",
                table: "CoOfrs",
                newName: "GoogleColorCode");
        }
    }
}
