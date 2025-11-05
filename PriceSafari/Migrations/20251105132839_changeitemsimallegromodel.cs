using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class changeitemsimallegromodel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CommissionAfter_Simulated",
                table: "AllegroPriceBridgeItems");

            migrationBuilder.DropColumn(
                name: "MarginAmountAfter_Simulated",
                table: "AllegroPriceBridgeItems");

            migrationBuilder.DropColumn(
                name: "MarginAmountAfter_Verified",
                table: "AllegroPriceBridgeItems");

            migrationBuilder.DropColumn(
                name: "MarginAmountBefore",
                table: "AllegroPriceBridgeItems");

            migrationBuilder.DropColumn(
                name: "MarginPercentAfter_Simulated",
                table: "AllegroPriceBridgeItems");

            migrationBuilder.DropColumn(
                name: "MarginPercentAfter_Verified",
                table: "AllegroPriceBridgeItems");

            migrationBuilder.RenameColumn(
                name: "MarginPercentBefore",
                table: "AllegroPriceBridgeItems",
                newName: "MarginPrice");

            migrationBuilder.AddColumn<bool>(
                name: "IncludeCommissionInMargin",
                table: "AllegroPriceBridgeItems",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IncludeCommissionInMargin",
                table: "AllegroPriceBridgeItems");

            migrationBuilder.RenameColumn(
                name: "MarginPrice",
                table: "AllegroPriceBridgeItems",
                newName: "MarginPercentBefore");

            migrationBuilder.AddColumn<decimal>(
                name: "CommissionAfter_Simulated",
                table: "AllegroPriceBridgeItems",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MarginAmountAfter_Simulated",
                table: "AllegroPriceBridgeItems",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MarginAmountAfter_Verified",
                table: "AllegroPriceBridgeItems",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MarginAmountBefore",
                table: "AllegroPriceBridgeItems",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MarginPercentAfter_Simulated",
                table: "AllegroPriceBridgeItems",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MarginPercentAfter_Verified",
                table: "AllegroPriceBridgeItems",
                type: "decimal(18,2)",
                nullable: true);
        }
    }
}
