using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TenantRelationalIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_businesses_organizations_OrganizationId",
                table: "businesses");

            migrationBuilder.DropForeignKey(
                name: "FK_categories_categories_ParentCategoryId",
                table: "categories");

            migrationBuilder.DropForeignKey(
                name: "FK_inventory_transactions_locations_LocationId",
                table: "inventory_transactions");

            migrationBuilder.DropForeignKey(
                name: "FK_inventory_transactions_products_ProductId",
                table: "inventory_transactions");

            migrationBuilder.DropForeignKey(
                name: "FK_inventory_transactions_warehouses_WarehouseId",
                table: "inventory_transactions");

            migrationBuilder.DropForeignKey(
                name: "FK_invoice_sequences_locations_LocationId",
                table: "invoice_sequences");

            migrationBuilder.DropForeignKey(
                name: "FK_locations_businesses_BusinessId",
                table: "locations");

            migrationBuilder.DropForeignKey(
                name: "FK_locations_organizations_OrganizationId",
                table: "locations");

            migrationBuilder.DropForeignKey(
                name: "FK_payments_sales_SaleId",
                table: "payments");

            migrationBuilder.DropForeignKey(
                name: "FK_products_categories_CategoryId",
                table: "products");

            migrationBuilder.DropForeignKey(
                name: "FK_products_tax_categories_TaxCategoryId",
                table: "products");

            migrationBuilder.DropForeignKey(
                name: "FK_sale_lines_products_ProductId",
                table: "sale_lines");

            migrationBuilder.DropForeignKey(
                name: "FK_sale_lines_sales_SaleId",
                table: "sale_lines");

            migrationBuilder.DropForeignKey(
                name: "FK_sales_customers_CustomerId",
                table: "sales");

            migrationBuilder.DropForeignKey(
                name: "FK_sales_locations_LocationId",
                table: "sales");

            migrationBuilder.DropForeignKey(
                name: "FK_sales_terminals_TerminalId",
                table: "sales");

            migrationBuilder.DropForeignKey(
                name: "FK_terminals_locations_LocationId",
                table: "terminals");

            migrationBuilder.DropForeignKey(
                name: "FK_warehouses_locations_LocationId",
                table: "warehouses");

            migrationBuilder.DropIndex(
                name: "IX_warehouses_LocationId",
                table: "warehouses");

            migrationBuilder.DropIndex(
                name: "IX_terminals_LocationId",
                table: "terminals");

            migrationBuilder.DropIndex(
                name: "IX_sales_CustomerId",
                table: "sales");

            migrationBuilder.DropIndex(
                name: "IX_sales_LocationId",
                table: "sales");

            migrationBuilder.DropIndex(
                name: "IX_sales_TerminalId",
                table: "sales");

            migrationBuilder.DropIndex(
                name: "IX_sale_lines_ProductId",
                table: "sale_lines");

            migrationBuilder.DropIndex(
                name: "IX_sale_lines_SaleId",
                table: "sale_lines");

            migrationBuilder.DropIndex(
                name: "IX_products_CategoryId",
                table: "products");

            migrationBuilder.DropIndex(
                name: "IX_products_TaxCategoryId",
                table: "products");

            migrationBuilder.DropIndex(
                name: "IX_payments_SaleId",
                table: "payments");

            migrationBuilder.DropIndex(
                name: "IX_locations_BusinessId",
                table: "locations");

            migrationBuilder.DropIndex(
                name: "IX_locations_OrganizationId",
                table: "locations");

            migrationBuilder.DropIndex(
                name: "IX_invoice_sequences_LocationId",
                table: "invoice_sequences");

            migrationBuilder.DropIndex(
                name: "IX_inventory_transactions_LocationId",
                table: "inventory_transactions");

            migrationBuilder.DropIndex(
                name: "IX_inventory_transactions_ProductId",
                table: "inventory_transactions");

            migrationBuilder.DropIndex(
                name: "IX_inventory_transactions_WarehouseId",
                table: "inventory_transactions");

            migrationBuilder.DropIndex(
                name: "IX_categories_ParentCategoryId",
                table: "categories");

            migrationBuilder.DropIndex(
                name: "IX_businesses_OrganizationId",
                table: "businesses");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_warehouses_TenantId_Id",
                table: "warehouses",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_users_TenantId_Id",
                table: "users",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_terminals_TenantId_Id",
                table: "terminals",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_tax_categories_TenantId_Id",
                table: "tax_categories",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_sales_TenantId_Id",
                table: "sales",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_sale_lines_TenantId_Id",
                table: "sale_lines",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_roles_TenantId_Id",
                table: "roles",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_products_TenantId_Id",
                table: "products",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_payments_TenantId_Id",
                table: "payments",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_outbox_events_TenantId_Id",
                table: "outbox_events",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_organizations_TenantId_Id",
                table: "organizations",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_locations_TenantId_Id",
                table: "locations",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_inventory_transactions_TenantId_Id",
                table: "inventory_transactions",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_customers_TenantId_Id",
                table: "customers",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_categories_TenantId_Id",
                table: "categories",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_businesses_TenantId_Id",
                table: "businesses",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_audit_logs_TenantId_Id",
                table: "audit_logs",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_warehouses_TenantId_LocationId",
                table: "warehouses",
                columns: new[] { "TenantId", "LocationId" });

            migrationBuilder.CreateIndex(
                name: "IX_terminals_TenantId_LocationId",
                table: "terminals",
                columns: new[] { "TenantId", "LocationId" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_TenantId_CustomerId",
                table: "sales",
                columns: new[] { "TenantId", "CustomerId" });

            migrationBuilder.CreateIndex(
                name: "IX_sale_lines_TenantId_ProductId",
                table: "sale_lines",
                columns: new[] { "TenantId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_products_TenantId_CategoryId",
                table: "products",
                columns: new[] { "TenantId", "CategoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_products_TenantId_TaxCategoryId",
                table: "products",
                columns: new[] { "TenantId", "TaxCategoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_payments_TenantId_SaleId",
                table: "payments",
                columns: new[] { "TenantId", "SaleId" });

            migrationBuilder.CreateIndex(
                name: "IX_locations_TenantId_BusinessId",
                table: "locations",
                columns: new[] { "TenantId", "BusinessId" });

            migrationBuilder.CreateIndex(
                name: "IX_locations_TenantId_OrganizationId",
                table: "locations",
                columns: new[] { "TenantId", "OrganizationId" });

            migrationBuilder.CreateIndex(
                name: "IX_inventory_transactions_TenantId_LocationId",
                table: "inventory_transactions",
                columns: new[] { "TenantId", "LocationId" });

            migrationBuilder.CreateIndex(
                name: "IX_inventory_transactions_TenantId_ProductId",
                table: "inventory_transactions",
                columns: new[] { "TenantId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_categories_TenantId_ParentCategoryId",
                table: "categories",
                columns: new[] { "TenantId", "ParentCategoryId" });

            migrationBuilder.AddForeignKey(
                name: "FK_businesses_organizations_TenantId_OrganizationId",
                table: "businesses",
                columns: new[] { "TenantId", "OrganizationId" },
                principalTable: "organizations",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_categories_categories_TenantId_ParentCategoryId",
                table: "categories",
                columns: new[] { "TenantId", "ParentCategoryId" },
                principalTable: "categories",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_inventory_transactions_locations_TenantId_LocationId",
                table: "inventory_transactions",
                columns: new[] { "TenantId", "LocationId" },
                principalTable: "locations",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_inventory_transactions_products_TenantId_ProductId",
                table: "inventory_transactions",
                columns: new[] { "TenantId", "ProductId" },
                principalTable: "products",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_inventory_transactions_warehouses_TenantId_WarehouseId",
                table: "inventory_transactions",
                columns: new[] { "TenantId", "WarehouseId" },
                principalTable: "warehouses",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_invoice_sequences_locations_TenantId_LocationId",
                table: "invoice_sequences",
                columns: new[] { "TenantId", "LocationId" },
                principalTable: "locations",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_locations_businesses_TenantId_BusinessId",
                table: "locations",
                columns: new[] { "TenantId", "BusinessId" },
                principalTable: "businesses",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_locations_organizations_TenantId_OrganizationId",
                table: "locations",
                columns: new[] { "TenantId", "OrganizationId" },
                principalTable: "organizations",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_payments_sales_TenantId_SaleId",
                table: "payments",
                columns: new[] { "TenantId", "SaleId" },
                principalTable: "sales",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_products_categories_TenantId_CategoryId",
                table: "products",
                columns: new[] { "TenantId", "CategoryId" },
                principalTable: "categories",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_products_tax_categories_TenantId_TaxCategoryId",
                table: "products",
                columns: new[] { "TenantId", "TaxCategoryId" },
                principalTable: "tax_categories",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_sale_lines_products_TenantId_ProductId",
                table: "sale_lines",
                columns: new[] { "TenantId", "ProductId" },
                principalTable: "products",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_sale_lines_sales_TenantId_SaleId",
                table: "sale_lines",
                columns: new[] { "TenantId", "SaleId" },
                principalTable: "sales",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_sales_customers_TenantId_CustomerId",
                table: "sales",
                columns: new[] { "TenantId", "CustomerId" },
                principalTable: "customers",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_sales_locations_TenantId_LocationId",
                table: "sales",
                columns: new[] { "TenantId", "LocationId" },
                principalTable: "locations",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_sales_terminals_TenantId_TerminalId",
                table: "sales",
                columns: new[] { "TenantId", "TerminalId" },
                principalTable: "terminals",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_terminals_locations_TenantId_LocationId",
                table: "terminals",
                columns: new[] { "TenantId", "LocationId" },
                principalTable: "locations",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_warehouses_locations_TenantId_LocationId",
                table: "warehouses",
                columns: new[] { "TenantId", "LocationId" },
                principalTable: "locations",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_businesses_organizations_TenantId_OrganizationId",
                table: "businesses");

            migrationBuilder.DropForeignKey(
                name: "FK_categories_categories_TenantId_ParentCategoryId",
                table: "categories");

            migrationBuilder.DropForeignKey(
                name: "FK_inventory_transactions_locations_TenantId_LocationId",
                table: "inventory_transactions");

            migrationBuilder.DropForeignKey(
                name: "FK_inventory_transactions_products_TenantId_ProductId",
                table: "inventory_transactions");

            migrationBuilder.DropForeignKey(
                name: "FK_inventory_transactions_warehouses_TenantId_WarehouseId",
                table: "inventory_transactions");

            migrationBuilder.DropForeignKey(
                name: "FK_invoice_sequences_locations_TenantId_LocationId",
                table: "invoice_sequences");

            migrationBuilder.DropForeignKey(
                name: "FK_locations_businesses_TenantId_BusinessId",
                table: "locations");

            migrationBuilder.DropForeignKey(
                name: "FK_locations_organizations_TenantId_OrganizationId",
                table: "locations");

            migrationBuilder.DropForeignKey(
                name: "FK_payments_sales_TenantId_SaleId",
                table: "payments");

            migrationBuilder.DropForeignKey(
                name: "FK_products_categories_TenantId_CategoryId",
                table: "products");

            migrationBuilder.DropForeignKey(
                name: "FK_products_tax_categories_TenantId_TaxCategoryId",
                table: "products");

            migrationBuilder.DropForeignKey(
                name: "FK_sale_lines_products_TenantId_ProductId",
                table: "sale_lines");

            migrationBuilder.DropForeignKey(
                name: "FK_sale_lines_sales_TenantId_SaleId",
                table: "sale_lines");

            migrationBuilder.DropForeignKey(
                name: "FK_sales_customers_TenantId_CustomerId",
                table: "sales");

            migrationBuilder.DropForeignKey(
                name: "FK_sales_locations_TenantId_LocationId",
                table: "sales");

            migrationBuilder.DropForeignKey(
                name: "FK_sales_terminals_TenantId_TerminalId",
                table: "sales");

            migrationBuilder.DropForeignKey(
                name: "FK_terminals_locations_TenantId_LocationId",
                table: "terminals");

            migrationBuilder.DropForeignKey(
                name: "FK_warehouses_locations_TenantId_LocationId",
                table: "warehouses");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_warehouses_TenantId_Id",
                table: "warehouses");

            migrationBuilder.DropIndex(
                name: "IX_warehouses_TenantId_LocationId",
                table: "warehouses");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_users_TenantId_Id",
                table: "users");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_terminals_TenantId_Id",
                table: "terminals");

            migrationBuilder.DropIndex(
                name: "IX_terminals_TenantId_LocationId",
                table: "terminals");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_tax_categories_TenantId_Id",
                table: "tax_categories");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_sales_TenantId_Id",
                table: "sales");

            migrationBuilder.DropIndex(
                name: "IX_sales_TenantId_CustomerId",
                table: "sales");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_sale_lines_TenantId_Id",
                table: "sale_lines");

            migrationBuilder.DropIndex(
                name: "IX_sale_lines_TenantId_ProductId",
                table: "sale_lines");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_roles_TenantId_Id",
                table: "roles");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_products_TenantId_Id",
                table: "products");

            migrationBuilder.DropIndex(
                name: "IX_products_TenantId_CategoryId",
                table: "products");

            migrationBuilder.DropIndex(
                name: "IX_products_TenantId_TaxCategoryId",
                table: "products");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_payments_TenantId_Id",
                table: "payments");

            migrationBuilder.DropIndex(
                name: "IX_payments_TenantId_SaleId",
                table: "payments");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_outbox_events_TenantId_Id",
                table: "outbox_events");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_organizations_TenantId_Id",
                table: "organizations");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_locations_TenantId_Id",
                table: "locations");

            migrationBuilder.DropIndex(
                name: "IX_locations_TenantId_BusinessId",
                table: "locations");

            migrationBuilder.DropIndex(
                name: "IX_locations_TenantId_OrganizationId",
                table: "locations");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_inventory_transactions_TenantId_Id",
                table: "inventory_transactions");

            migrationBuilder.DropIndex(
                name: "IX_inventory_transactions_TenantId_LocationId",
                table: "inventory_transactions");

            migrationBuilder.DropIndex(
                name: "IX_inventory_transactions_TenantId_ProductId",
                table: "inventory_transactions");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_customers_TenantId_Id",
                table: "customers");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_categories_TenantId_Id",
                table: "categories");

            migrationBuilder.DropIndex(
                name: "IX_categories_TenantId_ParentCategoryId",
                table: "categories");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_businesses_TenantId_Id",
                table: "businesses");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_audit_logs_TenantId_Id",
                table: "audit_logs");

            migrationBuilder.CreateIndex(
                name: "IX_warehouses_LocationId",
                table: "warehouses",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_terminals_LocationId",
                table: "terminals",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_sales_CustomerId",
                table: "sales",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_sales_LocationId",
                table: "sales",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_sales_TerminalId",
                table: "sales",
                column: "TerminalId");

            migrationBuilder.CreateIndex(
                name: "IX_sale_lines_ProductId",
                table: "sale_lines",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_sale_lines_SaleId",
                table: "sale_lines",
                column: "SaleId");

            migrationBuilder.CreateIndex(
                name: "IX_products_CategoryId",
                table: "products",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_products_TaxCategoryId",
                table: "products",
                column: "TaxCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_payments_SaleId",
                table: "payments",
                column: "SaleId");

            migrationBuilder.CreateIndex(
                name: "IX_locations_BusinessId",
                table: "locations",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_locations_OrganizationId",
                table: "locations",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_invoice_sequences_LocationId",
                table: "invoice_sequences",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_transactions_LocationId",
                table: "inventory_transactions",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_transactions_ProductId",
                table: "inventory_transactions",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_transactions_WarehouseId",
                table: "inventory_transactions",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_categories_ParentCategoryId",
                table: "categories",
                column: "ParentCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_businesses_OrganizationId",
                table: "businesses",
                column: "OrganizationId");

            migrationBuilder.AddForeignKey(
                name: "FK_businesses_organizations_OrganizationId",
                table: "businesses",
                column: "OrganizationId",
                principalTable: "organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_categories_categories_ParentCategoryId",
                table: "categories",
                column: "ParentCategoryId",
                principalTable: "categories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_inventory_transactions_locations_LocationId",
                table: "inventory_transactions",
                column: "LocationId",
                principalTable: "locations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_inventory_transactions_products_ProductId",
                table: "inventory_transactions",
                column: "ProductId",
                principalTable: "products",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_inventory_transactions_warehouses_WarehouseId",
                table: "inventory_transactions",
                column: "WarehouseId",
                principalTable: "warehouses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_invoice_sequences_locations_LocationId",
                table: "invoice_sequences",
                column: "LocationId",
                principalTable: "locations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_locations_businesses_BusinessId",
                table: "locations",
                column: "BusinessId",
                principalTable: "businesses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_locations_organizations_OrganizationId",
                table: "locations",
                column: "OrganizationId",
                principalTable: "organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_payments_sales_SaleId",
                table: "payments",
                column: "SaleId",
                principalTable: "sales",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_products_categories_CategoryId",
                table: "products",
                column: "CategoryId",
                principalTable: "categories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_products_tax_categories_TaxCategoryId",
                table: "products",
                column: "TaxCategoryId",
                principalTable: "tax_categories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_sale_lines_products_ProductId",
                table: "sale_lines",
                column: "ProductId",
                principalTable: "products",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_sale_lines_sales_SaleId",
                table: "sale_lines",
                column: "SaleId",
                principalTable: "sales",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_sales_customers_CustomerId",
                table: "sales",
                column: "CustomerId",
                principalTable: "customers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_sales_locations_LocationId",
                table: "sales",
                column: "LocationId",
                principalTable: "locations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_sales_terminals_TerminalId",
                table: "sales",
                column: "TerminalId",
                principalTable: "terminals",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_terminals_locations_LocationId",
                table: "terminals",
                column: "LocationId",
                principalTable: "locations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_warehouses_locations_LocationId",
                table: "warehouses",
                column: "LocationId",
                principalTable: "locations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
