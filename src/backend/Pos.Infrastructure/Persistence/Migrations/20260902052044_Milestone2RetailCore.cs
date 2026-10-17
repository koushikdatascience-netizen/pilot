using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Milestone2RetailCore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AmountDue",
                table: "payments",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "AmountTendered",
                table: "payments",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ChangeDue",
                table: "payments",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "CashierMaxDiscountPercent",
                table: "locations",
                type: "numeric(9,6)",
                precision: 9,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ManagerMaxDiscountPercent",
                table: "locations",
                type: "numeric(9,6)",
                precision: 9,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "StateCode",
                table: "customers",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AmountDue",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "AmountTendered",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "ChangeDue",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "CashierMaxDiscountPercent",
                table: "locations");

            migrationBuilder.DropColumn(
                name: "ManagerMaxDiscountPercent",
                table: "locations");

            migrationBuilder.DropColumn(
                name: "StateCode",
                table: "customers");
        }
    }
}
