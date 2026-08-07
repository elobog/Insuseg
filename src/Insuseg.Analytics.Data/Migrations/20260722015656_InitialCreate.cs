using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Insuseg.Analytics.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SalesPersons",
                columns: table => new
                {
                    SalesEmployeeCode = table.Column<int>(type: "int", nullable: false),
                    SalesEmployeeName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesPersons", x => x.SalesEmployeeCode);
                });

            migrationBuilder.CreateTable(
                name: "Sales",
                columns: table => new
                {
                    DocEntry = table.Column<int>(type: "int", nullable: false),
                    DocNum = table.Column<int>(type: "int", nullable: false),
                    CardCode = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: false),
                    CardName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    SaleDate = table.Column<DateOnly>(type: "date", nullable: false),
                    SalesPersonCode = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sales", x => x.DocEntry);
                    table.ForeignKey(
                        name: "FK_Sales_SalesPersons_SalesPersonCode",
                        column: x => x.SalesPersonCode,
                        principalTable: "SalesPersons",
                        principalColumn: "SalesEmployeeCode");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Sales_SalesPersonCode",
                table: "Sales",
                column: "SalesPersonCode");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Sales");

            migrationBuilder.DropTable(
                name: "SalesPersons");
        }
    }
}
