using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class FixGoogleLinksNullable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
           
            migrationBuilder.Sql("UPDATE Stores SET CollectGoogleStoreLinks = 0 WHERE CollectGoogleStoreLinks IS NULL");

            
            migrationBuilder.AlterColumn<bool>(
                name: "CollectGoogleStoreLinks",
                table: "Stores",
                type: "bit",
                nullable: false,
                defaultValue: false, 
                oldClrType: typeof(bool?), 
                oldType: "bit",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            
            migrationBuilder.AlterColumn<bool?>(
                name: "CollectGoogleStoreLinks",
                table: "Stores",
                type: "bit",
                nullable: true,
                oldClrType: typeof(bool),
                oldType: "bit");
        }
    }
}