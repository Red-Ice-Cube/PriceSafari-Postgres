using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class setpresets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CompetitorPresets",
                columns: table => new
                {
                    PresetId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StoreId = table.Column<int>(type: "int", nullable: false),
                    SourceGoogle = table.Column<bool>(type: "bit", nullable: false),
                    SourceCeneo = table.Column<bool>(type: "bit", nullable: false),
                    UseUnmarkedStores = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompetitorPresets", x => x.PresetId);
                    table.ForeignKey(
                        name: "FK_CompetitorPresets_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "StoreId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CompetitorPresetItems",
                columns: table => new
                {
                    CompetitorPresetItemId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PresetId = table.Column<int>(type: "int", nullable: false),
                    StoreName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsGoogle = table.Column<bool>(type: "bit", nullable: false),
                    UseCompetitor = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompetitorPresetItems", x => x.CompetitorPresetItemId);
                    table.ForeignKey(
                        name: "FK_CompetitorPresetItems_CompetitorPresets_PresetId",
                        column: x => x.PresetId,
                        principalTable: "CompetitorPresets",
                        principalColumn: "PresetId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CompetitorPresetItems_PresetId",
                table: "CompetitorPresetItems",
                column: "PresetId");

            migrationBuilder.CreateIndex(
                name: "IX_CompetitorPresets_StoreId",
                table: "CompetitorPresets",
                column: "StoreId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CompetitorPresetItems");

            migrationBuilder.DropTable(
                name: "CompetitorPresets");
        }
    }
}
