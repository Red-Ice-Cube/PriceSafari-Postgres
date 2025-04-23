using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class productmapsfield : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CeneoDeliveryXMLPrice",
                table: "ProductMaps",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CeneoXMLPrice",
                table: "ProductMaps",
                type: "decimal(18,2)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CeneoDeliveryXMLPrice",
                table: "ProductMaps");

            migrationBuilder.DropColumn(
                name: "CeneoXMLPrice",
                table: "ProductMaps");
        }
    }
}
