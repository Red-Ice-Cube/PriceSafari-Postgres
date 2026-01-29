using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class newwwa : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "GoogleGeneratorsCount",
                table: "Settings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "HeadLessForGoogleGenerators",
                table: "Settings",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GoogleGeneratorsCount",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "HeadLessForGoogleGenerators",
                table: "Settings");
        }
    }
}
