using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RetailCorrectionsAndDrawers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                INSERT INTO role_permissions ("RoleId", "Permission")
                SELECT r."RoleId", p.permission FROM role_permissions r
                CROSS JOIN (VALUES ('ProcessReturns'), ('VoidSales'), ('ManageCashDrawer')) AS p(permission)
                WHERE r."Permission" = 'ManageSettings' ON CONFLICT DO NOTHING;
                """);
            migrationBuilder.CreateTable(
                name: "cash_drawer_sessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TerminalId = table.Column<Guid>(type: "uuid", nullable: false),
                    OpenCommandId = table.Column<Guid>(type: "uuid", nullable: false),
                    OpenRequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OpeningFloat = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    OpenedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    OpenedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CloseCommandId = table.Column<Guid>(type: "uuid", nullable: true),
                    CloseRequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ClosedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ClosedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ExpectedAtClose = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    CountedAtClose = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    Variance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    CloseNote = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cash_drawer_sessions", x => x.Id);
                    table.UniqueConstraint("AK_cash_drawer_sessions_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_cash_drawer_sessions_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_cash_drawer_sessions_terminals_TenantId_TerminalId",
                        columns: x => new { x.TenantId, x.TerminalId },
                        principalTable: "terminals",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sale_corrections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SaleId = table.Column<Guid>(type: "uuid", nullable: false),
                    TerminalId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientGeneratedId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sale_corrections", x => x.Id);
                    table.UniqueConstraint("AK_sale_corrections_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_sale_corrections_sales_TenantId_SaleId",
                        columns: x => new { x.TenantId, x.SaleId },
                        principalTable: "sales",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sale_corrections_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sale_corrections_terminals_TenantId_TerminalId",
                        columns: x => new { x.TenantId, x.TerminalId },
                        principalTable: "terminals",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "cash_drawer_movements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AmountDelta = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cash_drawer_movements", x => x.Id);
                    table.UniqueConstraint("AK_cash_drawer_movements_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_cash_drawer_movements_cash_drawer_sessions_TenantId_Session~",
                        columns: x => new { x.TenantId, x.SessionId },
                        principalTable: "cash_drawer_sessions",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_cash_drawer_movements_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sale_correction_lines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CorrectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SaleLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: true),
                    Quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    Restock = table.Column<bool>(type: "boolean", nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TaxableAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CgstAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    SgstAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    IgstAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sale_correction_lines", x => x.Id);
                    table.UniqueConstraint("AK_sale_correction_lines_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_sale_correction_lines_sale_corrections_TenantId_CorrectionId",
                        columns: x => new { x.TenantId, x.CorrectionId },
                        principalTable: "sale_corrections",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sale_correction_lines_sale_lines_TenantId_SaleLineId",
                        columns: x => new { x.TenantId, x.SaleLineId },
                        principalTable: "sale_lines",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sale_correction_lines_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sale_correction_lines_warehouses_TenantId_WarehouseId",
                        columns: x => new { x.TenantId, x.WarehouseId },
                        principalTable: "warehouses",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sale_refunds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CorrectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Method = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Reference = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sale_refunds", x => x.Id);
                    table.UniqueConstraint("AK_sale_refunds_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_sale_refunds_sale_corrections_TenantId_CorrectionId",
                        columns: x => new { x.TenantId, x.CorrectionId },
                        principalTable: "sale_corrections",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sale_refunds_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_cash_drawer_movements_TenantId",
                table: "cash_drawer_movements",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_cash_drawer_movements_TenantId_SessionId_SourceId",
                table: "cash_drawer_movements",
                columns: new[] { "TenantId", "SessionId", "SourceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_cash_drawer_sessions_TenantId",
                table: "cash_drawer_sessions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_cash_drawer_sessions_TenantId_TerminalId",
                table: "cash_drawer_sessions",
                columns: new[] { "TenantId", "TerminalId" },
                unique: true,
                filter: "\"ClosedAtUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_cash_drawer_sessions_TenantId_TerminalId_OpenCommandId",
                table: "cash_drawer_sessions",
                columns: new[] { "TenantId", "TerminalId", "OpenCommandId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sale_correction_lines_TenantId",
                table: "sale_correction_lines",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_sale_correction_lines_TenantId_CorrectionId_SaleLineId",
                table: "sale_correction_lines",
                columns: new[] { "TenantId", "CorrectionId", "SaleLineId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sale_correction_lines_TenantId_SaleLineId",
                table: "sale_correction_lines",
                columns: new[] { "TenantId", "SaleLineId" });

            migrationBuilder.CreateIndex(
                name: "IX_sale_correction_lines_TenantId_WarehouseId",
                table: "sale_correction_lines",
                columns: new[] { "TenantId", "WarehouseId" });

            migrationBuilder.CreateIndex(
                name: "IX_sale_corrections_TenantId",
                table: "sale_corrections",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_sale_corrections_TenantId_Number",
                table: "sale_corrections",
                columns: new[] { "TenantId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sale_corrections_TenantId_OccurredAtUtc",
                table: "sale_corrections",
                columns: new[] { "TenantId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_sale_corrections_TenantId_SaleId",
                table: "sale_corrections",
                columns: new[] { "TenantId", "SaleId" });

            migrationBuilder.CreateIndex(
                name: "IX_sale_corrections_TenantId_TerminalId_ClientGeneratedId",
                table: "sale_corrections",
                columns: new[] { "TenantId", "TerminalId", "ClientGeneratedId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sale_refunds_TenantId",
                table: "sale_refunds",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_sale_refunds_TenantId_CorrectionId_Method",
                table: "sale_refunds",
                columns: new[] { "TenantId", "CorrectionId", "Method" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM role_permissions WHERE \"Permission\" IN ('ProcessReturns', 'VoidSales', 'ManageCashDrawer');");
            migrationBuilder.DropTable(
                name: "cash_drawer_movements");

            migrationBuilder.DropTable(
                name: "sale_correction_lines");

            migrationBuilder.DropTable(
                name: "sale_refunds");

            migrationBuilder.DropTable(
                name: "cash_drawer_sessions");

            migrationBuilder.DropTable(
                name: "sale_corrections");
        }
    }
}
