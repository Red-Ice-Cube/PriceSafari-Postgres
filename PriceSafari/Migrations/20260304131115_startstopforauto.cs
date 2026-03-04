using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class startstopforauto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsTimeLimited",
                table: "AutomationRules",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "ScheduledEndDate",
                table: "AutomationRules",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ScheduledStartDate",
                table: "AutomationRules",
                type: "date",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsTimeLimited",
                table: "AutomationRules");

            migrationBuilder.DropColumn(
                name: "ScheduledEndDate",
                table: "AutomationRules");

            migrationBuilder.DropColumn(
                name: "ScheduledStartDate",
                table: "AutomationRules");
        }
    }
}
