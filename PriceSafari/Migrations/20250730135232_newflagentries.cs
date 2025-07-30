using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class newflagentries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProductFlags_AllegroProducts_AllegroProductClassAllegroProductId",
                table: "ProductFlags");

            migrationBuilder.RenameColumn(
                name: "AllegroProductClassAllegroProductId",
                table: "ProductFlags",
                newName: "AllegroProductId");

            migrationBuilder.RenameIndex(
                name: "IX_ProductFlags_AllegroProductClassAllegroProductId",
                table: "ProductFlags",
                newName: "IX_ProductFlags_AllegroProductId");

            migrationBuilder.AlterColumn<int>(
                name: "FlagId",
                table: "ProductFlags",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int")
                .OldAnnotation("Relational:ColumnOrder", 1);

            migrationBuilder.AlterColumn<int>(
                name: "ProductId",
                table: "ProductFlags",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int")
                .OldAnnotation("Relational:ColumnOrder", 0);

            migrationBuilder.AddColumn<int>(
                name: "ProductFlagId",
                table: "ProductFlags",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddForeignKey(
                name: "FK_ProductFlags_AllegroProducts_AllegroProductId",
                table: "ProductFlags",
                column: "AllegroProductId",
                principalTable: "AllegroProducts",
                principalColumn: "AllegroProductId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProductFlags_AllegroProducts_AllegroProductId",
                table: "ProductFlags");

            migrationBuilder.DropColumn(
                name: "ProductFlagId",
                table: "ProductFlags");

            migrationBuilder.RenameColumn(
                name: "AllegroProductId",
                table: "ProductFlags",
                newName: "AllegroProductClassAllegroProductId");

            migrationBuilder.RenameIndex(
                name: "IX_ProductFlags_AllegroProductId",
                table: "ProductFlags",
                newName: "IX_ProductFlags_AllegroProductClassAllegroProductId");

            migrationBuilder.AlterColumn<int>(
                name: "FlagId",
                table: "ProductFlags",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int")
                .Annotation("Relational:ColumnOrder", 1);

            migrationBuilder.AlterColumn<int>(
                name: "ProductId",
                table: "ProductFlags",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int")
                .Annotation("Relational:ColumnOrder", 0);

            migrationBuilder.AddForeignKey(
                name: "FK_ProductFlags_AllegroProducts_AllegroProductClassAllegroProductId",
                table: "ProductFlags",
                column: "AllegroProductClassAllegroProductId",
                principalTable: "AllegroProducts",
                principalColumn: "AllegroProductId");
        }
    }
}
