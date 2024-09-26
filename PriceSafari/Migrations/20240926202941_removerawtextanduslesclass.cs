using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class removerawtextanduslesclass : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PriceData_ScrapeRuns_ScrapeRunId",
                table: "PriceData");

            migrationBuilder.DropTable(
                name: "ScrapeRuns");

            migrationBuilder.DropIndex(
                name: "IX_PriceData_ScrapeRunId",
                table: "PriceData");

            migrationBuilder.DropColumn(
                name: "RawPriceText",
                table: "PriceData");

            migrationBuilder.DropColumn(
                name: "RawPriceWithDeliveryText",
                table: "PriceData");

            migrationBuilder.DropColumn(
                name: "ScrapeRunId",
                table: "PriceData");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RawPriceText",
                table: "PriceData",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RawPriceWithDeliveryText",
                table: "PriceData",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "ScrapeRunId",
                table: "PriceData",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ScrapeRuns",
                columns: table => new
                {
                    ScrapeRunId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StartTime = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScrapeRuns", x => x.ScrapeRunId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PriceData_ScrapeRunId",
                table: "PriceData",
                column: "ScrapeRunId");

            migrationBuilder.AddForeignKey(
                name: "FK_PriceData_ScrapeRuns_ScrapeRunId",
                table: "PriceData",
                column: "ScrapeRunId",
                principalTable: "ScrapeRuns",
                principalColumn: "ScrapeRunId");
        }
    }
}
