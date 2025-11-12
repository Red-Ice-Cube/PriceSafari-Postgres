using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class extendedlogicforapiallegrobot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "InvitationPrice",
                table: "AllegroPriceHistoryExtendedInfos",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsInvitationActive",
                table: "AllegroPriceHistoryExtendedInfos",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsSubsidyActive",
                table: "AllegroPriceHistoryExtendedInfos",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "InvitationPrice",
                table: "AllegroOffersToScrape",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsInvitationActive",
                table: "AllegroOffersToScrape",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsSubsidyActive",
                table: "AllegroOffersToScrape",
                type: "bit",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InvitationPrice",
                table: "AllegroPriceHistoryExtendedInfos");

            migrationBuilder.DropColumn(
                name: "IsInvitationActive",
                table: "AllegroPriceHistoryExtendedInfos");

            migrationBuilder.DropColumn(
                name: "IsSubsidyActive",
                table: "AllegroPriceHistoryExtendedInfos");

            migrationBuilder.DropColumn(
                name: "InvitationPrice",
                table: "AllegroOffersToScrape");

            migrationBuilder.DropColumn(
                name: "IsInvitationActive",
                table: "AllegroOffersToScrape");

            migrationBuilder.DropColumn(
                name: "IsSubsidyActive",
                table: "AllegroOffersToScrape");
        }
    }
}
