using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class cncifrpmitoue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UserPaymentDatas_AspNetUsers_UserId",
                table: "UserPaymentDatas");

            migrationBuilder.DropIndex(
                name: "IX_UserPaymentDatas_UserId",
                table: "UserPaymentDatas");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "UserPaymentDatas");

            migrationBuilder.AddColumn<int>(
                name: "StoreId",
                table: "UserPaymentDatas",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_UserPaymentDatas_StoreId",
                table: "UserPaymentDatas",
                column: "StoreId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_UserPaymentDatas_Stores_StoreId",
                table: "UserPaymentDatas",
                column: "StoreId",
                principalTable: "Stores",
                principalColumn: "StoreId",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UserPaymentDatas_Stores_StoreId",
                table: "UserPaymentDatas");

            migrationBuilder.DropIndex(
                name: "IX_UserPaymentDatas_StoreId",
                table: "UserPaymentDatas");

            migrationBuilder.DropColumn(
                name: "StoreId",
                table: "UserPaymentDatas");

            migrationBuilder.AddColumn<string>(
                name: "UserId",
                table: "UserPaymentDatas",
                type: "nvarchar(450)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_UserPaymentDatas_UserId",
                table: "UserPaymentDatas",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_UserPaymentDatas_AspNetUsers_UserId",
                table: "UserPaymentDatas",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
