using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceTracker.Migrations
{
    /// <inheritdoc />
    public partial class Cascadedellete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PriceHistories_ScrapHistories_ScrapHistoryId",
                table: "PriceHistories");

            migrationBuilder.AddForeignKey(
                name: "FK_PriceHistories_ScrapHistories_ScrapHistoryId",
                table: "PriceHistories",
                column: "ScrapHistoryId",
                principalTable: "ScrapHistories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PriceHistories_ScrapHistories_ScrapHistoryId",
                table: "PriceHistories");

            migrationBuilder.AddForeignKey(
                name: "FK_PriceHistories_ScrapHistories_ScrapHistoryId",
                table: "PriceHistories",
                column: "ScrapHistoryId",
                principalTable: "ScrapHistories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
