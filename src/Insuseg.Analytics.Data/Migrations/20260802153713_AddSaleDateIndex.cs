using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Insuseg.Analytics.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSaleDateIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Sales_SaleDate",
                table: "Sales",
                column: "SaleDate");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Sales_SaleDate",
                table: "Sales");
        }
    }
}
