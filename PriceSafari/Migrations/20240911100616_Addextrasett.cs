using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class Addextrasett : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ScrapSemaphoreSlim",
                table: "Settings",
                newName: "WarmUpTime");

            migrationBuilder.AddColumn<bool>(
                name: "HeadLess",
                table: "Settings",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HeadLess",
                table: "Settings");

            migrationBuilder.RenameColumn(
                name: "WarmUpTime",
                table: "Settings",
                newName: "ScrapSemaphoreSlim");
        }
    }
}
