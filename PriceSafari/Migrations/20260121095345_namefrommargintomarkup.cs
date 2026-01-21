using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class namefrommargintomarkup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "SkipIfMarginLimited",
                table: "AutomationRules",
                newName: "SkipIfMarkupLimited");

            migrationBuilder.RenameColumn(
                name: "MinimalMarginValue",
                table: "AutomationRules",
                newName: "MinimalMarkupValue");

            migrationBuilder.RenameColumn(
                name: "MaxMarginValue",
                table: "AutomationRules",
                newName: "MaxMarkupValue");

            migrationBuilder.RenameColumn(
                name: "IsMinimalMarginPercent",
                table: "AutomationRules",
                newName: "IsMinimalMarkupPercent");

            migrationBuilder.RenameColumn(
                name: "IsMaxMarginPercent",
                table: "AutomationRules",
                newName: "IsMaxMarkupPercent");

            migrationBuilder.RenameColumn(
                name: "EnforceMinimalMargin",
                table: "AutomationRules",
                newName: "EnforceMinimalMarkup");

            migrationBuilder.RenameColumn(
                name: "EnforceMaxMargin",
                table: "AutomationRules",
                newName: "EnforceMaxMarkup");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "SkipIfMarkupLimited",
                table: "AutomationRules",
                newName: "SkipIfMarginLimited");

            migrationBuilder.RenameColumn(
                name: "MinimalMarkupValue",
                table: "AutomationRules",
                newName: "MinimalMarginValue");

            migrationBuilder.RenameColumn(
                name: "MaxMarkupValue",
                table: "AutomationRules",
                newName: "MaxMarginValue");

            migrationBuilder.RenameColumn(
                name: "IsMinimalMarkupPercent",
                table: "AutomationRules",
                newName: "IsMinimalMarginPercent");

            migrationBuilder.RenameColumn(
                name: "IsMaxMarkupPercent",
                table: "AutomationRules",
                newName: "IsMaxMarginPercent");

            migrationBuilder.RenameColumn(
                name: "EnforceMinimalMarkup",
                table: "AutomationRules",
                newName: "EnforceMinimalMargin");

            migrationBuilder.RenameColumn(
                name: "EnforceMaxMarkup",
                table: "AutomationRules",
                newName: "EnforceMaxMargin");
        }
    }
}
