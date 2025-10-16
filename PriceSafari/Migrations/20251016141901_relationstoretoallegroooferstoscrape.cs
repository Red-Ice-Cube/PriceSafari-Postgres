using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class relationstoretoallegroooferstoscrape : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "StoreId",
                table: "AllegroOffersToScrape",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_AllegroOffersToScrape_StoreId",
                table: "AllegroOffersToScrape",
                column: "StoreId");

            migrationBuilder.AddForeignKey(
                name: "FK_AllegroOffersToScrape_Stores_StoreId",
                table: "AllegroOffersToScrape",
                column: "StoreId",
                principalTable: "Stores",
                principalColumn: "StoreId",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AllegroOffersToScrape_Stores_StoreId",
                table: "AllegroOffersToScrape");

            migrationBuilder.DropIndex(
                name: "IX_AllegroOffersToScrape_StoreId",
                table: "AllegroOffersToScrape");

            migrationBuilder.DropColumn(
                name: "StoreId",
                table: "AllegroOffersToScrape");
        }
    }
}
