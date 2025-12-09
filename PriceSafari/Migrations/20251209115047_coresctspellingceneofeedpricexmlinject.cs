using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class coresctspellingceneofeedpricexmlinject : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "UseGoogleCeneoFeedPrice",
                table: "Stores",
                newName: "UseCeneoXMLFeedPrice");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "UseCeneoXMLFeedPrice",
                table: "Stores",
                newName: "UseGoogleCeneoFeedPrice");
        }
    }
}
