using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class deletestore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ScrapHistories_Stores_StoreId",
                table: "ScrapHistories");

            migrationBuilder.AddForeignKey(
                name: "FK_ScrapHistories_Stores_StoreId",
                table: "ScrapHistories",
                column: "StoreId",
                principalTable: "Stores",
                principalColumn: "StoreId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ScrapHistories_Stores_StoreId",
                table: "ScrapHistories");

            migrationBuilder.AddForeignKey(
                name: "FK_ScrapHistories_Stores_StoreId",
                table: "ScrapHistories",
                column: "StoreId",
                principalTable: "Stores",
                principalColumn: "StoreId",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
