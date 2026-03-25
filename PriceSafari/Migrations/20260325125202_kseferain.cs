using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class kseferain : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "UseKSeF",
                table: "Stores",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsExportedToKSeF",
                table: "Invoices",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "KSeFErrorMessage",
                table: "Invoices",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "KSeFExportDate",
                table: "Invoices",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "KSeFInvoiceNumber",
                table: "Invoices",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "KSeFReferenceNumber",
                table: "Invoices",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "KSeFStatus",
                table: "Invoices",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UseKSeF",
                table: "Stores");

            migrationBuilder.DropColumn(
                name: "IsExportedToKSeF",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "KSeFErrorMessage",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "KSeFExportDate",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "KSeFInvoiceNumber",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "KSeFReferenceNumber",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "KSeFStatus",
                table: "Invoices");
        }
    }
}
