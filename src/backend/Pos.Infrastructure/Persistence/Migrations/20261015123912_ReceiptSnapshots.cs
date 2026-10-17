using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReceiptSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReceiptJson",
                table: "sales",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReceiptJson",
                table: "sales");
        }
    }
}
