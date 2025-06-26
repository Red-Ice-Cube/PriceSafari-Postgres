using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class newvaluespending : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "PendingStoreUrl",
                table: "AspNetUsers",
                newName: "PendingStoreNameGoogle");

            migrationBuilder.AddColumn<string>(
                name: "PendingStoreNameCeneo",
                table: "AspNetUsers",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PendingStoreNameCeneo",
                table: "AspNetUsers");

            migrationBuilder.RenameColumn(
                name: "PendingStoreNameGoogle",
                table: "AspNetUsers",
                newName: "PendingStoreUrl");
        }
    }
}
