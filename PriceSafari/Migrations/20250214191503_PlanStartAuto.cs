using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class PlanStartAuto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TaskComplete",
                table: "ScheduleTasks");

            migrationBuilder.AddColumn<string>(
                name: "SessionName",
                table: "ScheduleTasks",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SessionName",
                table: "ScheduleTasks");

            migrationBuilder.AddColumn<bool>(
                name: "TaskComplete",
                table: "ScheduleTasks",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }
    }
}
