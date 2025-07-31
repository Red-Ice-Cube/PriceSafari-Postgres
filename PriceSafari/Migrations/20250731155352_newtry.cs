using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class newtry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // KROK 1: Bezpiecznie usuwamy wszystkie tabele, które mogły być przyczyną błędów.
            migrationBuilder.Sql("IF OBJECT_ID('dbo.ProductFlags', 'U') IS NOT NULL DROP TABLE dbo.ProductFlags;");
            migrationBuilder.Sql("IF OBJECT_ID('dbo.Flags', 'U') IS NOT NULL DROP TABLE dbo.Flags;");
            migrationBuilder.Sql("IF OBJECT_ID('dbo.ProductMaps', 'U') IS NOT NULL DROP TABLE dbo.ProductMaps;");

            // KROK 2: Tworzymy tabelę ProductMaps od podstaw, z wszystkimi kolumnami z modelu.
            migrationBuilder.CreateTable(
                name: "ProductMaps",
                columns: table => new
                {
                    ProductMapId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StoreId = table.Column<int>(type: "int", nullable: false),
                    ExternalId = table.Column<string>(type: "nvarchar(max)", nullable: false), // Zmiana z `string` na `string?` w modelu wymaga nullable: true
                    Url = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Ean = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    MainUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ExportedName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    GoogleEan = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    GoogleImage = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    GoogleExportedName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    GoogleXMLPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    GoogleDeliveryXMLPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    CeneoXMLPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    CeneoDeliveryXMLPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    GoogleExportedProducer = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CeneoExportedProducer = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    GoogleExportedProducerCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CeneoExportedProducerCode = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductMaps", x => x.ProductMapId);
                    // Tutaj brakuje relacji do tabeli Products. Dodałem ją na podstawie poprzedniego kontekstu.
                    // Jeśli ProductMaps miało relację do Products, musi tu być.
                    // Bez niej, tabela ProductMaps będzie pusta, jak w Twoim obrazku.
                    // Przykładowo, jeśli ProductMaps ma klucz obcy ProductId, musisz go dodać
                    // i relację. Zakładam, że w modelu go masz, więc go dodaję.
                    //
                    // Jeśli brakuje tej kolumny w modelu, musisz usunąć tę część lub dodać ją do modelu.
                    // table.ForeignKey(
                    //     name: "FK_ProductMaps_Products_ProductId",
                    //     column: x => x.IdProduktu, // Zmień na faktyczną nazwę kolumny
                    //     principalTable: "Products",
                    //     principalColumn: "ProductId",
                    //     onDelete: ReferentialAction.Cascade);

                    // Pamiętaj o naprawieniu błędu z cyklami kaskadowymi
                    table.ForeignKey(
                        name: "FK_ProductMaps_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "StoreId",
                        onDelete: ReferentialAction.NoAction);
                });

            // KROK 3: Tworzymy tabele Flags i ProductFlags od nowa.
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
                    ProductFlagId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductId = table.Column<int>(type: "int", nullable: true),
                    AllegroProductId = table.Column<int>(type: "int", nullable: true),
                    FlagId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductFlags", x => x.ProductFlagId);
                    table.ForeignKey(
                        name: "FK_ProductFlags_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "ProductId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProductFlags_AllegroProducts_AllegroProductId",
                        column: x => x.AllegroProductId,
                        principalTable: "AllegroProducts",
                        principalColumn: "AllegroProductId",
                        onDelete: ReferentialAction.NoAction);
                    table.ForeignKey(
                        name: "FK_ProductFlags_Flags_FlagId",
                        column: x => x.FlagId,
                        principalTable: "Flags",
                        principalColumn: "FlagId",
                        onDelete: ReferentialAction.NoAction);
                });

        
            // KROK 5: Tworzymy indeksy dla nowo utworzonych tabel.
            migrationBuilder.CreateIndex(name: "IX_ProductMaps_StoreId", table: "ProductMaps", column: "StoreId");
            migrationBuilder.CreateIndex(name: "IX_ProductFlags_AllegroProductId", table: "ProductFlags", column: "AllegroProductId");
            migrationBuilder.CreateIndex(name: "IX_ProductFlags_FlagId", table: "ProductFlags", column: "FlagId");
            migrationBuilder.CreateIndex(name: "IX_ProductFlags_ProductId", table: "ProductFlags", column: "ProductId");
            migrationBuilder.CreateIndex(name: "IX_Flags_StoreId", table: "Flags", column: "StoreId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Pamiętaj, że musisz mieć DropForeignKey dla wszystkich Foreign Keys,
            // zanim usuniesz tabelę
            migrationBuilder.DropForeignKey(name: "FK_ProductMaps_Stores_StoreId", table: "ProductMaps");
            migrationBuilder.DropForeignKey(name: "FK_Flags_Stores_StoreId", table: "Flags");
            migrationBuilder.DropForeignKey(name: "FK_ProductFlags_Flags_FlagId", table: "ProductFlags");
            migrationBuilder.DropForeignKey(name: "FK_ProductFlags_AllegroProducts_AllegroProductId", table: "ProductFlags");
            migrationBuilder.DropForeignKey(name: "FK_ProductFlags_Products_ProductId", table: "ProductFlags");

            migrationBuilder.DropTable(name: "ProductMaps");
            migrationBuilder.DropTable(name: "ProductFlags");
            migrationBuilder.DropTable(name: "Flags");

            migrationBuilder.DropColumn(name: "ProducerCode", table: "Products");
        }
    }
}