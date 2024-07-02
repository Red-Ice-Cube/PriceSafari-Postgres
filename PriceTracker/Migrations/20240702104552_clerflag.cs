using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceTracker.Migrations
{
    /// <inheritdoc />
    public partial class clerflag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Flags_Stores_StoreId",
                table: "Flags");

            migrationBuilder.DropIndex(
                name: "IX_Flags_StoreId",
                table: "Flags");

            migrationBuilder.AddColumn<int>(
                name: "StoreClassStoreId",
                table: "Flags",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Flags_StoreClassStoreId",
                table: "Flags",
                column: "StoreClassStoreId");

            migrationBuilder.AddForeignKey(
                name: "FK_Flags_Stores_StoreClassStoreId",
                table: "Flags",
                column: "StoreClassStoreId",
                principalTable: "Stores",
                principalColumn: "StoreId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Flags_Stores_StoreClassStoreId",
                table: "Flags");

            migrationBuilder.DropIndex(
                name: "IX_Flags_StoreClassStoreId",
                table: "Flags");

            migrationBuilder.DropColumn(
                name: "StoreClassStoreId",
                table: "Flags");

            migrationBuilder.CreateIndex(
                name: "IX_Flags_StoreId",
                table: "Flags",
                column: "StoreId");

            migrationBuilder.AddForeignKey(
                name: "FK_Flags_Stores_StoreId",
                table: "Flags",
                column: "StoreId",
                principalTable: "Stores",
                principalColumn: "StoreId",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
