using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class Addallegrooffercolector : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "StoreNameAllegro",
                table: "Stores",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AllegroProductClassAllegroProductId",
                table: "ProductFlags",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AllegroProducts",
                columns: table => new
                {
                    AllegroProductId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StoreId = table.Column<int>(type: "int", nullable: false),
                    AllegroProductName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AllegroOfferUrl = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AddedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    MarginPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AllegroProducts", x => x.AllegroProductId);
                    table.ForeignKey(
                        name: "FK_AllegroProducts_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "StoreId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductFlags_AllegroProductClassAllegroProductId",
                table: "ProductFlags",
                column: "AllegroProductClassAllegroProductId");

            migrationBuilder.CreateIndex(
                name: "IX_AllegroProducts_StoreId",
                table: "AllegroProducts",
                column: "StoreId");

            migrationBuilder.AddForeignKey(
                name: "FK_ProductFlags_AllegroProducts_AllegroProductClassAllegroProductId",
                table: "ProductFlags",
                column: "AllegroProductClassAllegroProductId",
                principalTable: "AllegroProducts",
                principalColumn: "AllegroProductId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProductFlags_AllegroProducts_AllegroProductClassAllegroProductId",
                table: "ProductFlags");

            migrationBuilder.DropTable(
                name: "AllegroProducts");

            migrationBuilder.DropIndex(
                name: "IX_ProductFlags_AllegroProductClassAllegroProductId",
                table: "ProductFlags");

            migrationBuilder.DropColumn(
                name: "StoreNameAllegro",
                table: "Stores");

            migrationBuilder.DropColumn(
                name: "AllegroProductClassAllegroProductId",
                table: "ProductFlags");
        }
    }
}
