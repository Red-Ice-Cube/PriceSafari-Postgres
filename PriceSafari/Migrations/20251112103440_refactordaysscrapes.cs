using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class refactordaysscrapes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "RemainingScrapes",
                table: "Stores",
                newName: "RemainingDays");

            migrationBuilder.RenameColumn(
                name: "ScrapesPerInvoice",
                table: "Plans",
                newName: "DaysPerInvoice");

            migrationBuilder.RenameColumn(
                name: "ScrapesIncluded",
                table: "Invoices",
                newName: "DaysIncluded");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "RemainingDays",
                table: "Stores",
                newName: "RemainingScrapes");

            migrationBuilder.RenameColumn(
                name: "DaysPerInvoice",
                table: "Plans",
                newName: "ScrapesPerInvoice");

            migrationBuilder.RenameColumn(
                name: "DaysIncluded",
                table: "Invoices",
                newName: "ScrapesIncluded");
        }
    }
}
