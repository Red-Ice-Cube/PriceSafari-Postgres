using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class addnewxmlfieldgoogle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "GoogleDeliveryXMLPrice",
                table: "ProductMaps",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "GoogleXMLPrice",
                table: "ProductMaps",
                type: "decimal(18,2)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GoogleDeliveryXMLPrice",
                table: "ProductMaps");

            migrationBuilder.DropColumn(
                name: "GoogleXMLPrice",
                table: "ProductMaps");
        }
    }
}
