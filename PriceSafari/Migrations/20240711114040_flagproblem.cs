using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class flagproblem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProductFlags_Flags_FlagId",
                table: "ProductFlags");

            migrationBuilder.DropColumn(
                name: "ProductFlagId",
                table: "ProductFlags");

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
                name: "FK_ProductFlags_Flags_FlagId",
                table: "ProductFlags",
                column: "FlagId",
                principalTable: "Flags",
                principalColumn: "FlagId",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProductFlags_Flags_FlagId",
                table: "ProductFlags");

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
                name: "FK_ProductFlags_Flags_FlagId",
                table: "ProductFlags",
                column: "FlagId",
                principalTable: "Flags",
                principalColumn: "FlagId");
        }
    }
}
