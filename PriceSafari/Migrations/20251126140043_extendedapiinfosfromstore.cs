using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class extendedapiinfosfromstore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "FetchExtendedData",
                table: "Stores",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "CoOfrStoreDatas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CoOfrClassId = table.Column<int>(type: "int", nullable: false),
                    StoreId = table.Column<int>(type: "int", nullable: false),
                    ProductExternalId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ExtendedDataApiPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    IsApiProcessed = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CoOfrStoreDatas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CoOfrStoreDatas_CoOfrs_CoOfrClassId",
                        column: x => x.CoOfrClassId,
                        principalTable: "CoOfrs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CoOfrStoreDatas_CoOfrClassId",
                table: "CoOfrStoreDatas",
                column: "CoOfrClassId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CoOfrStoreDatas");

            migrationBuilder.DropColumn(
                name: "FetchExtendedData",
                table: "Stores");
        }
    }
}
