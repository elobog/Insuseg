using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Insuseg.Analytics.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesSourceDocType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_Sales",
                table: "Sales");

            migrationBuilder.AddColumn<int>(
                name: "SourceDocType",
                table: "Sales",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddPrimaryKey(
                name: "PK_Sales",
                table: "Sales",
                columns: new[] { "DocEntry", "SourceDocType" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_Sales",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "SourceDocType",
                table: "Sales");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Sales",
                table: "Sales",
                column: "DocEntry");
        }
    }
}
