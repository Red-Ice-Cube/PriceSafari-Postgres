using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class AddStoreNavigationToFlags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Ten kod robi tylko to, co konieczne:
            // 1. Tworzy indeks na istniejącej kolumnie StoreId dla wydajności.
            // 2. Dodaje formalny klucz obcy (relację) do tabeli Stores.

            migrationBuilder.CreateIndex(
                name: "IX_Flags_StoreId",
                table: "Flags",
                column: "StoreId");

            migrationBuilder.AddForeignKey(
                name: "FK_Flags_Stores_StoreId",
                table: "Flags",
                column: "StoreId",
                principalTable: "Stores",
                principalColumn: "StoreId",
                onDelete: ReferentialAction.Cascade); // Usunięcie sklepu usunie jego flagi
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Metoda Down cofa operacje z metody Up:
            // 1. Usuwa klucz obcy.
            // 2. Usuwa indeks.

            migrationBuilder.DropForeignKey(
                name: "FK_Flags_Stores_StoreId",
                table: "Flags");

            migrationBuilder.DropIndex(
                name: "IX_Flags_StoreId",
                table: "Flags");
        }
    }
}