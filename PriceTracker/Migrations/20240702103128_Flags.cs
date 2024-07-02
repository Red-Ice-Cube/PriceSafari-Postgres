using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceTracker.Migrations
{
    /// <inheritdoc />
    public partial class Flags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Flags",
                columns: table => new
                {
                    FlagId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FlagName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FlagColor = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    StoreId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Flags", x => x.FlagId);
                    table.ForeignKey(
                        name: "FK_Flags_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "StoreId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProductFlags",
                columns: table => new
                {
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    FlagId = table.Column<int>(type: "int", nullable: false),
                    ProductFlagId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductFlags", x => new { x.ProductId, x.FlagId });
                    table.ForeignKey(
                        name: "FK_ProductFlags_Flags_FlagId",
                        column: x => x.FlagId,
                        principalTable: "Flags",
                        principalColumn: "FlagId");
                    table.ForeignKey(
                        name: "FK_ProductFlags_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "ProductId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Flags_StoreId",
                table: "Flags",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductFlags_FlagId",
                table: "ProductFlags",
                column: "FlagId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductFlags");

            migrationBuilder.DropTable(
                name: "Flags");
        }
    }
}
