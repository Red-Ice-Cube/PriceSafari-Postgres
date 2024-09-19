using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class raportconection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PriceSafariReportId",
                table: "GlobalPriceReports",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_GlobalPriceReports_PriceSafariReportId",
                table: "GlobalPriceReports",
                column: "PriceSafariReportId");

            migrationBuilder.AddForeignKey(
                name: "FK_GlobalPriceReports_PriceSafariReports_PriceSafariReportId",
                table: "GlobalPriceReports",
                column: "PriceSafariReportId",
                principalTable: "PriceSafariReports",
                principalColumn: "ReportId",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_GlobalPriceReports_PriceSafariReports_PriceSafariReportId",
                table: "GlobalPriceReports");

            migrationBuilder.DropIndex(
                name: "IX_GlobalPriceReports_PriceSafariReportId",
                table: "GlobalPriceReports");

            migrationBuilder.DropColumn(
                name: "PriceSafariReportId",
                table: "GlobalPriceReports");
        }
    }
}
