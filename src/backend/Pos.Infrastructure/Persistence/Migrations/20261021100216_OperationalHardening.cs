using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OperationalHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AuthVersion",
                table: "users",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            // Login accepts email without a tenant selector, so email identity must be globally unambiguous.
            migrationBuilder.Sql("CREATE UNIQUE INDEX \"IX_users_NormalizedEmail\" ON users (lower(\"Email\"));");
            migrationBuilder.Sql("""
                INSERT INTO role_permissions ("RoleId", "Permission")
                SELECT "RoleId", 'ManageUsers' FROM role_permissions WHERE "Permission" = 'ManageSettings'
                ON CONFLICT DO NOTHING;
                INSERT INTO role_permissions ("RoleId", "Permission")
                SELECT "RoleId", 'ViewAudit' FROM role_permissions WHERE "Permission" = 'ManageSettings'
                ON CONFLICT DO NOTHING;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_audit_logs_TenantId_OccurredAtUtc_Id",
                table: "audit_logs",
                columns: new[] { "TenantId", "OccurredAtUtc", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX \"IX_users_NormalizedEmail\";");
            migrationBuilder.Sql("DELETE FROM role_permissions WHERE \"Permission\" IN ('ManageUsers', 'ViewAudit');");
            migrationBuilder.DropIndex(
                name: "IX_audit_logs_TenantId_OccurredAtUtc_Id",
                table: "audit_logs");

            migrationBuilder.DropColumn(
                name: "AuthVersion",
                table: "users");
        }
    }
}
