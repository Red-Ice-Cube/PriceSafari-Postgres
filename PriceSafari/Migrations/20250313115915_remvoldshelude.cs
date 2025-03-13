using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class remvoldshelude : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ScheduledTasks");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ScheduledTasks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CeneoIsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    CeneoScheduledTime = table.Column<TimeSpan>(type: "time", nullable: false),
                    GoogleIsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    GoogleScheduledTime = table.Column<TimeSpan>(type: "time", nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    ScheduledTime = table.Column<TimeSpan>(type: "time", nullable: false),
                    UrlIsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    UrlScheduledTime = table.Column<TimeSpan>(type: "time", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduledTasks", x => x.Id);
                });
        }
    }
}
