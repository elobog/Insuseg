using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Insuseg.Analytics.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddApplicationUserSalesPersonCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SalesPersonCode",
                table: "AspNetUsers",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SalesPersonCode",
                table: "AspNetUsers");
        }
    }
}
