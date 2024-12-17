using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class correctinspell : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "InvoicAutoMailSend",
                table: "UserPaymentDatas",
                newName: "InvoiceAutoMailSend");

            migrationBuilder.RenameColumn(
                name: "InvoicAutoMail",
                table: "UserPaymentDatas",
                newName: "InvoiceAutoMail");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "InvoiceAutoMailSend",
                table: "UserPaymentDatas",
                newName: "InvoicAutoMailSend");

            migrationBuilder.RenameColumn(
                name: "InvoiceAutoMail",
                table: "UserPaymentDatas",
                newName: "InvoicAutoMail");
        }
    }
}
