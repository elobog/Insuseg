using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Insuseg.Analytics.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDeliveryNoteLineSalesPersonCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SalesPersonCode",
                table: "DeliveryNoteLines",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryNoteLines_SalesPersonCode",
                table: "DeliveryNoteLines",
                column: "SalesPersonCode");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DeliveryNoteLines_SalesPersonCode",
                table: "DeliveryNoteLines");

            migrationBuilder.DropColumn(
                name: "SalesPersonCode",
                table: "DeliveryNoteLines");
        }
    }
}
