using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class Addclientmechanism : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AffiliateDescription",
                table: "AffiliateVerification");

            migrationBuilder.CreateTable(
                name: "ClientProfiles",
                columns: table => new
                {
                    ClientProfileId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CeneoProfileUrl = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CeneoProfileName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CeneoProfileEmail = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CeneoProfileTelephone = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CeneoProfileProductCount = table.Column<int>(type: "int", nullable: false),
                    CreationDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ScheduledMeetingDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientProfiles", x => x.ClientProfileId);
                    table.ForeignKey(
                        name: "FK_ClientProfiles_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClientProfiles_CreatedByUserId",
                table: "ClientProfiles",
                column: "CreatedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClientProfiles");

            migrationBuilder.AddColumn<string>(
                name: "AffiliateDescription",
                table: "AffiliateVerification",
                type: "nvarchar(max)",
                nullable: true);
        }
    }
}
