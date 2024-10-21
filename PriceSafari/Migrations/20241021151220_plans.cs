using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class plans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "DiscountPercentage",
                table: "Stores",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsInvoicePaid",
                table: "Stores",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "PlanEndDate",
                table: "Stores",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PlanId",
                table: "Stores",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PlanStartDate",
                table: "Stores",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Plans",
                columns: table => new
                {
                    PlanId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PlanName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    NetPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    DurationDays = table.Column<int>(type: "int", nullable: false),
                    IsTestPlan = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Plans", x => x.PlanId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Stores_PlanId",
                table: "Stores",
                column: "PlanId");

            migrationBuilder.AddForeignKey(
                name: "FK_Stores_Plans_PlanId",
                table: "Stores",
                column: "PlanId",
                principalTable: "Plans",
                principalColumn: "PlanId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Stores_Plans_PlanId",
                table: "Stores");

            migrationBuilder.DropTable(
                name: "Plans");

            migrationBuilder.DropIndex(
                name: "IX_Stores_PlanId",
                table: "Stores");

            migrationBuilder.DropColumn(
                name: "DiscountPercentage",
                table: "Stores");

            migrationBuilder.DropColumn(
                name: "IsInvoicePaid",
                table: "Stores");

            migrationBuilder.DropColumn(
                name: "PlanEndDate",
                table: "Stores");

            migrationBuilder.DropColumn(
                name: "PlanId",
                table: "Stores");

            migrationBuilder.DropColumn(
                name: "PlanStartDate",
                table: "Stores");
        }
    }
}
