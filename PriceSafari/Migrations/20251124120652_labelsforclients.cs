using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class labelsforclients : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ContactLabels",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    HexColor = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContactLabels", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ClientProfileContactLabel",
                columns: table => new
                {
                    ClientProfilesClientProfileId = table.Column<int>(type: "int", nullable: false),
                    LabelsId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientProfileContactLabel", x => new { x.ClientProfilesClientProfileId, x.LabelsId });
                    table.ForeignKey(
                        name: "FK_ClientProfileContactLabel_ClientProfiles_ClientProfilesClientProfileId",
                        column: x => x.ClientProfilesClientProfileId,
                        principalTable: "ClientProfiles",
                        principalColumn: "ClientProfileId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ClientProfileContactLabel_ContactLabels_LabelsId",
                        column: x => x.LabelsId,
                        principalTable: "ContactLabels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClientProfileContactLabel_LabelsId",
                table: "ClientProfileContactLabel",
                column: "LabelsId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClientProfileContactLabel");

            migrationBuilder.DropTable(
                name: "ContactLabels");
        }
    }
}
