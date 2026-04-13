using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class xmlpricemapping : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CopyXMLPrices",
                table: "Stores",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "CopyXmlPriceMappings",
                columns: table => new
                {
                    StoreId = table.Column<int>(type: "integer", nullable: false),
                    KeyField = table.Column<int>(type: "integer", nullable: false),
                    ProductNodeXPath = table.Column<string>(type: "text", nullable: true),
                    KeyXPath = table.Column<string>(type: "text", nullable: true),
                    PriceXPath = table.Column<string>(type: "text", nullable: true),
                    PriceWithShippingXPath = table.Column<string>(type: "text", nullable: true),
                    InStockXPath = table.Column<string>(type: "text", nullable: true),
                    InStockMarkerValue = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CopyXmlPriceMappings", x => x.StoreId);
                    table.ForeignKey(
                        name: "FK_CopyXmlPriceMappings_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "StoreId",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CopyXmlPriceMappings");

            migrationBuilder.DropColumn(
                name: "CopyXMLPrices",
                table: "Stores");
        }
    }
}
