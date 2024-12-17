using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class additionaldatatuserpaymentdata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "InvoicAutoMail",
                table: "UserPaymentDatas",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "InvoicAutoMailSend",
                table: "UserPaymentDatas",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InvoicAutoMail",
                table: "UserPaymentDatas");

            migrationBuilder.DropColumn(
                name: "InvoicAutoMailSend",
                table: "UserPaymentDatas");
        }
    }
}
