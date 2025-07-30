using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class FinalFlagsAndProductsSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // KROK 1: Upewniamy się, że nie ma starych wersji tabel.
            migrationBuilder.Sql("IF OBJECT_ID('dbo.ProductFlags', 'U') IS NOT NULL DROP TABLE dbo.ProductFlags;");
            migrationBuilder.Sql("IF OBJECT_ID('dbo.Flags', 'U') IS NOT NULL DROP TABLE dbo.Flags;");

            // KROK 2: Tworzymy obie tabele na nowo z poprawnym schematem.

            // Tworzenie tabeli Flags
            migrationBuilder.CreateTable(
                name: "Flags",
                columns: table => new
                {
                    FlagId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FlagName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FlagColor = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    StoreId = table.Column<int>(type: "int", nullable: false),
                    IsMarketplace = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Flags", x => x.FlagId);
                });

            // Tworzenie tabeli ProductFlags z poprawnymi relacjami
            migrationBuilder.CreateTable(
                name: "ProductFlags",
                columns: table => new
                {
                    ProductFlagId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductId = table.Column<int>(type: "int", nullable: true),
                    AllegroProductId = table.Column<int>(type: "int", nullable: true),
                    FlagId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductFlags", x => x.ProductFlagId);

                    // JEDYNA GŁÓWNA ścieżka kaskadowa
                    table.ForeignKey(
                        name: "FK_ProductFlags_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "ProductId",
                        onDelete: ReferentialAction.Cascade);

                    // Podrzędna relacja (bez kaskady)
                    table.ForeignKey(
                        name: "FK_ProductFlags_AllegroProducts_AllegroProductId",
                        column: x => x.AllegroProductId,
                        principalTable: "AllegroProducts",
                        principalColumn: "AllegroProductId",
                        onDelete: ReferentialAction.NoAction);

                    // Podrzędna relacja (bez kaskady)
                    table.ForeignKey(
                        name: "FK_ProductFlags_Flags_FlagId",
                        column: x => x.FlagId,
                        principalTable: "Flags",
                        principalColumn: "FlagId",
                        onDelete: ReferentialAction.NoAction);
                });

            // Tworzenie indeksów
            migrationBuilder.CreateIndex(
                name: "IX_ProductFlags_AllegroProductId",
                table: "ProductFlags",
                column: "AllegroProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductFlags_FlagId",
                table: "ProductFlags",
                column: "FlagId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductFlags_ProductId",
                table: "ProductFlags",
                column: "ProductId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ProductFlags");
            migrationBuilder.DropTable(name: "Flags");
        }
    }
}