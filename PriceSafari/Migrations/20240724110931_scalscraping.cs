using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class scalscraping : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CoOfrPriceHistories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CoOfrClassId = table.Column<int>(type: "int", nullable: false),
                    StoreName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Price = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    IsBidding = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Position = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ShippingCostNum = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    AvailabilityNum = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CoOfrPriceHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CoOfrPriceHistories_CoOfrs_CoOfrClassId",
                        column: x => x.CoOfrClassId,
                        principalTable: "CoOfrs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CoOfrPriceHistories_CoOfrClassId",
                table: "CoOfrPriceHistories",
                column: "CoOfrClassId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CoOfrPriceHistories");
        }
    }
}
