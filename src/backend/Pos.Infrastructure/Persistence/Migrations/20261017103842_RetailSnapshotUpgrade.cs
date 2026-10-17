using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RetailSnapshotUpgrade : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "HsnSacSnapshot",
                table: "sale_lines",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);
            migrationBuilder.Sql("""
                UPDATE payments SET "AmountDue" = "Amount", "AmountTendered" = "Amount"
                WHERE "AmountDue" = 0 AND "AmountTendered" = 0;
                UPDATE payments SET "Method" = 'Credit' WHERE "Method" = 'Account';
                UPDATE locations SET "CashierMaxDiscountPercent" = 0.10, "ManagerMaxDiscountPercent" = 1.00
                WHERE "CashierMaxDiscountPercent" = 0 AND "ManagerMaxDiscountPercent" = 0;
                DELETE FROM role_permissions
                WHERE "RoleId" = '77777777-7777-7777-7777-777777777777' AND "Permission" = 'ApplyDiscount';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HsnSacSnapshot",
                table: "sale_lines");
        }
    }
}
