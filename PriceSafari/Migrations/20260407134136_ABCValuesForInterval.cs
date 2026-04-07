using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class ABCValuesForInterval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "StepLetter",
                table: "IntervalPriceExecutionItems",
                type: "character varying(1)",
                maxLength: 1,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "StepLetter",
                table: "IntervalPriceExecutionBatches",
                type: "character varying(1)",
                maxLength: 1,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StepLetter",
                table: "IntervalPriceExecutionItems");

            migrationBuilder.DropColumn(
                name: "StepLetter",
                table: "IntervalPriceExecutionBatches");
        }
    }
}
