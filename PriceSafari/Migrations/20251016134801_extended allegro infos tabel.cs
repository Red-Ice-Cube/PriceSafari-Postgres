using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class extendedallegroinfostabel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AllegroPriceHistoryExtendedInfos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AllegroProductId = table.Column<int>(type: "int", nullable: false),
                    ScrapHistoryId = table.Column<int>(type: "int", nullable: false),
                    ApiAllegroPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    ApiAllegroPriceFromUser = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    ApiAllegroCommission = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    AnyPromoActive = table.Column<bool>(type: "bit", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AllegroPriceHistoryExtendedInfos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AllegroPriceHistoryExtendedInfos_AllegroProducts_AllegroProductId",
                        column: x => x.AllegroProductId,
                        principalTable: "AllegroProducts",
                        principalColumn: "AllegroProductId");
                    table.ForeignKey(
                        name: "FK_AllegroPriceHistoryExtendedInfos_AllegroScrapeHistories_ScrapHistoryId",
                        column: x => x.ScrapHistoryId,
                        principalTable: "AllegroScrapeHistories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AllegroPriceHistoryExtendedInfos_AllegroProductId",
                table: "AllegroPriceHistoryExtendedInfos",
                column: "AllegroProductId");

            migrationBuilder.CreateIndex(
                name: "IX_AllegroPriceHistoryExtendedInfos_ScrapHistoryId",
                table: "AllegroPriceHistoryExtendedInfos",
                column: "ScrapHistoryId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AllegroPriceHistoryExtendedInfos");
        }
    }
}
