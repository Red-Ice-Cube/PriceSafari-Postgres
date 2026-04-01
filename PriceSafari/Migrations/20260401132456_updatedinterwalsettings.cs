using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class updatedinterwalsettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastExecutionDate",
                table: "IntervalPriceRules");

            migrationBuilder.DropColumn(
                name: "SortOrder",
                table: "IntervalPriceRules");

            migrationBuilder.DropColumn(
                name: "TotalExecutions",
                table: "IntervalPriceRules");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastExecutionDate",
                table: "IntervalPriceRules",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "IntervalPriceRules",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TotalExecutions",
                table: "IntervalPriceRules",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }
    }
}
