using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class raporttostore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PriceSafariReports_Stores_StoreId",
                table: "PriceSafariReports");

            migrationBuilder.AddForeignKey(
                name: "FK_PriceSafariReports_Stores_StoreId",
                table: "PriceSafariReports",
                column: "StoreId",
                principalTable: "Stores",
                principalColumn: "StoreId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PriceSafariReports_Stores_StoreId",
                table: "PriceSafariReports");

            migrationBuilder.AddForeignKey(
                name: "FK_PriceSafariReports_Stores_StoreId",
                table: "PriceSafariReports",
                column: "StoreId",
                principalTable: "Stores",
                principalColumn: "StoreId",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
