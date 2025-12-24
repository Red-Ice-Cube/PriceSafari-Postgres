using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class silingvaluesformaxmargin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "EnforceMaxMargin",
                table: "AutomationRules",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsMaxMarginPercent",
                table: "AutomationRules",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "MaxMarginValue",
                table: "AutomationRules",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EnforceMaxMargin",
                table: "AutomationRules");

            migrationBuilder.DropColumn(
                name: "IsMaxMarginPercent",
                table: "AutomationRules");

            migrationBuilder.DropColumn(
                name: "MaxMarginValue",
                table: "AutomationRules");
        }
    }
}
