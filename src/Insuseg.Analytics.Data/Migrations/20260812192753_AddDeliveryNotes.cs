using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Insuseg.Analytics.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDeliveryNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DeliveryNoteLines",
                columns: table => new
                {
                    DocEntry = table.Column<int>(type: "int", nullable: false),
                    LineNum = table.Column<int>(type: "int", nullable: false),
                    ItemCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    LineTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeliveryNoteLines", x => new { x.DocEntry, x.LineNum });
                });

            migrationBuilder.CreateTable(
                name: "DeliveryNotes",
                columns: table => new
                {
                    DocEntry = table.Column<int>(type: "int", nullable: false),
                    DocNum = table.Column<int>(type: "int", nullable: false),
                    CardCode = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: false),
                    CardName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DocDate = table.Column<DateOnly>(type: "date", nullable: false),
                    SalesPersonCode = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeliveryNotes", x => x.DocEntry);
                    table.ForeignKey(
                        name: "FK_DeliveryNotes_SalesPersons_SalesPersonCode",
                        column: x => x.SalesPersonCode,
                        principalTable: "SalesPersons",
                        principalColumn: "SalesEmployeeCode");
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryNotes_DocDate",
                table: "DeliveryNotes",
                column: "DocDate");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryNotes_SalesPersonCode",
                table: "DeliveryNotes",
                column: "SalesPersonCode");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeliveryNoteLines");

            migrationBuilder.DropTable(
                name: "DeliveryNotes");
        }
    }
}
