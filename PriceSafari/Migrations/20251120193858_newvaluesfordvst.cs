using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class newvaluesfordvst : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "InvoiceGeneratorEnabled",
                table: "DeviceStatuses",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "PaymentProcessorEnabled",
                table: "DeviceStatuses",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InvoiceGeneratorEnabled",
                table: "DeviceStatuses");

            migrationBuilder.DropColumn(
                name: "PaymentProcessorEnabled",
                table: "DeviceStatuses");
        }
    }
}
