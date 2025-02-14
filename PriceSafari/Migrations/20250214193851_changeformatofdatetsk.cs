using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class changeformatofdatetsk : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CompletedAt",
                table: "ScheduleTasks");

            migrationBuilder.AddColumn<TimeSpan>(
                name: "EndTime",
                table: "ScheduleTasks",
                type: "time",
                nullable: false,
                defaultValue: new TimeSpan(0, 0, 0, 0, 0));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EndTime",
                table: "ScheduleTasks");

            migrationBuilder.AddColumn<DateTime>(
                name: "CompletedAt",
                table: "ScheduleTasks",
                type: "datetime2",
                nullable: true);
        }
    }
}
