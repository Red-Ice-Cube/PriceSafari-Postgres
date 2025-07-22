using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class allegroscrapedoffersdata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsProcessing",
                table: "AllegroOffersToScrape",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "AllegroScrapedOffers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SellerName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Price = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    AllegroOfferToScrapeId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AllegroScrapedOffers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AllegroScrapedOffers_AllegroOffersToScrape_AllegroOfferToScrapeId",
                        column: x => x.AllegroOfferToScrapeId,
                        principalTable: "AllegroOffersToScrape",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AllegroScrapedOffers_AllegroOfferToScrapeId",
                table: "AllegroScrapedOffers",
                column: "AllegroOfferToScrapeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AllegroScrapedOffers");

            migrationBuilder.DropColumn(
                name: "IsProcessing",
                table: "AllegroOffersToScrape");
        }
    }
}
