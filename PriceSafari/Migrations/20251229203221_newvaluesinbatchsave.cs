using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class newvaluesinbatchsave : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TargetMetCount",
                table: "PriceBridgeBatches",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TargetUnmetCount",
                table: "PriceBridgeBatches",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TotalProductsCount",
                table: "PriceBridgeBatches",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TargetMetCount",
                table: "AllegroPriceBridgeBatches",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TargetUnmetCount",
                table: "AllegroPriceBridgeBatches",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TotalProductsCount",
                table: "AllegroPriceBridgeBatches",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TargetMetCount",
                table: "PriceBridgeBatches");

            migrationBuilder.DropColumn(
                name: "TargetUnmetCount",
                table: "PriceBridgeBatches");

            migrationBuilder.DropColumn(
                name: "TotalProductsCount",
                table: "PriceBridgeBatches");

            migrationBuilder.DropColumn(
                name: "TargetMetCount",
                table: "AllegroPriceBridgeBatches");

            migrationBuilder.DropColumn(
                name: "TargetUnmetCount",
                table: "AllegroPriceBridgeBatches");

            migrationBuilder.DropColumn(
                name: "TotalProductsCount",
                table: "AllegroPriceBridgeBatches");
        }
    }
}
