using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pos.Application.Auth;
using Pos.Application.Catalog;
using Pos.Application.Common;
using Pos.Application.Inventory;
using Pos.Application.Reporting;
using Pos.Application.Retail;
using Pos.Application.Sales;
using Pos.Application.Tenancy;
using Pos.Domain.Sales;
using Pos.Infrastructure.Persistence;
using Xunit;

namespace Pos.Postgres.Tests;

[Collection(PostgresCollection.Name)]
public sealed class RetailOperationsTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    [Fact]
    public async Task Partial_returns_reconcile_cash_without_rewriting_the_invoice_or_original_ledger()
    {
        await using var factory = await PostgresTestAppFactory.CreateAsync();
        using var client = await Login(factory);
        var boot = (await client.GetFromJsonAsync<BootstrapResponse>("/api/v1/tenancy/bootstrap", Json))!;
        var drawer = await Post<DrawerSessionResponse>(client, $"/api/v1/cash-drawers/terminals/{boot.TerminalId}/open", new OpenDrawerRequest(Guid.NewGuid(), 20));
        var sale = await Sale(client, boot, 3);
        drawer = (await client.GetFromJsonAsync<DrawerSessionResponse[]>($"/api/v1/cash-drawers/terminals/{boot.TerminalId}", Json))!.Single();
        Assert.Equal(50, drawer.ExpectedCash); // Float + allocation, not the 50 tendered.
        var paidIn = new DrawerMovementRequest(Guid.NewGuid(), "PaidIn", 5, "Float top-up");
        await Post<DrawerSessionResponse>(client, $"/api/v1/cash-drawers/{drawer.Id}/movements", paidIn);
        await Post<DrawerSessionResponse>(client, $"/api/v1/cash-drawers/{drawer.Id}/movements", paidIn);
        await Post<DrawerSessionResponse>(client, $"/api/v1/cash-drawers/{drawer.Id}/movements", new DrawerMovementRequest(Guid.NewGuid(), "PaidOut", 2, "Petty cash"));
        var first = Return(sale, boot.TerminalId, 1, true);
        var quote = await Post<CorrectionQuoteResponse>(client, $"/api/v1/sales/{sale.Id}/returns/quote", first);
        first = first with { Refunds = [new(PaymentMethod.Cash, quote.Total, null)] };
        var correction = await Post<CorrectionResponse>(client, $"/api/v1/sales/{sale.Id}/returns", first);
        Assert.True((await Post<CorrectionResponse>(client, $"/api/v1/sales/{sale.Id}/returns", first)).IdempotentReplay);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/v1/sales/{sale.Id}/returns", first with { Reason = "Different command" }, Json)).StatusCode);
        var last = Return(sale, boot.TerminalId, 2, false);
        var lastQuote = await Post<CorrectionQuoteResponse>(client, $"/api/v1/sales/{sale.Id}/returns/quote", last);
        await Post<CorrectionResponse>(client, $"/api/v1/sales/{sale.Id}/returns", last with { Refunds = [new(PaymentMethod.Cash, lastQuote.Total, null)] });
        var history = (await client.GetFromJsonAsync<CorrectionHistoryResponse>($"/api/v1/sales/{sale.Id}/corrections", Json))!;
        Assert.Equal("Returned", history.EffectiveStatus);
        Assert.Equal(sale.GrandTotal, history.Corrections.Sum(c => c.Total));
        Assert.Equal(sale.Lines.Single().TaxableAmount, history.Corrections.SelectMany(c => c.Lines).Sum(l => l.TaxableAmount));
        var report = (await client.GetFromJsonAsync<TodayReportResponse>("/api/v1/reports/today", Json))!;
        Assert.Equal(30, report.GrossRevenue); Assert.Equal(30, report.RefundTotal); Assert.Equal(0, report.Revenue);
        drawer = (await client.GetFromJsonAsync<DrawerSessionResponse[]>($"/api/v1/cash-drawers/terminals/{boot.TerminalId}", Json))!.Single();
        Assert.Equal(23, drawer.ExpectedCash); Assert.Equal(2, drawer.Movements.Count(m => m.Kind == "Refund"));
        var close = new CloseDrawerRequest(Guid.NewGuid(), 21, "Counted short");
        var closed = await Post<DrawerSessionResponse>(client, $"/api/v1/cash-drawers/{drawer.Id}/close", close);
        Assert.Equal(-2, closed.Variance); Assert.Equal(23, closed.ExpectedCash);
        Assert.Equal(closed, await Post<DrawerSessionResponse>(client, $"/api/v1/cash-drawers/{drawer.Id}/close", close), new DrawerComparer());
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<CommerceDbContext>();
        var original = await db.Sales.SingleAsync(s => s.Id == sale.Id);
        Assert.Equal(SaleStatus.Completed, original.Status); Assert.Equal(sale.InvoiceNumber, original.InvoiceNumber); Assert.Equal(sale.GrandTotal, original.GrandTotal);
        Assert.All(await db.Payments.Where(p => p.SaleId == sale.Id).ToArrayAsync(), p => Assert.Equal(PaymentStatus.Captured, p.Status));
        Assert.Equal(-3, await db.InventoryTransactions.Where(i => i.SourceDocumentId == sale.Id).SumAsync(i => i.QuantityDelta));
        Assert.Equal(1, await db.InventoryTransactions.Where(i => i.ProductId == sale.Lines[0].ProductId).SumAsync(i => i.QuantityDelta));
        Assert.Equal(2, await db.SaleCorrections.CountAsync()); Assert.Equal(1, await db.AuditLogs.CountAsync(a => a.EntityId == correction.Id));
    }

    [Fact]
    public async Task Concurrent_returns_cannot_refund_or_restock_the_last_unit_twice()
    {
        await using var factory = await PostgresTestAppFactory.CreateAsync(); using var client = await Login(factory);
        var boot = (await client.GetFromJsonAsync<BootstrapResponse>("/api/v1/tenancy/bootstrap", Json))!;
        await Post<DrawerSessionResponse>(client, $"/api/v1/cash-drawers/terminals/{boot.TerminalId}/open", new OpenDrawerRequest(Guid.NewGuid(), 100));
        var sale = await Sale(client, boot, 1);
        var request = Return(sale, boot.TerminalId, 1, true) with { Refunds = [new(PaymentMethod.Cash, sale.GrandTotal, null)] };
        var responses = await Task.WhenAll(client.PostAsJsonAsync($"/api/v1/sales/{sale.Id}/returns", request, Json), client.PostAsJsonAsync($"/api/v1/sales/{sale.Id}/returns", request with { ClientGeneratedId = Guid.NewGuid() }, Json));
        Assert.Single(responses, r => r.IsSuccessStatusCode); Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<CommerceDbContext>();
        Assert.Equal(1, await db.SaleCorrections.CountAsync()); Assert.Equal(1, await db.CashDrawerMovements.CountAsync(m => m.Kind == "Refund"));
        Assert.Equal(1, await db.InventoryTransactions.Where(i => i.ProductId == sale.Lines[0].ProductId).SumAsync(i => i.QuantityDelta));
    }

    [Fact]
    public async Task Refunds_use_historical_taxes_tenders_and_warehouse_even_after_catalog_changes()
    {
        await using var factory = await PostgresTestAppFactory.CreateAsync(); using var client = await Login(factory);
        var boot = (await client.GetFromJsonAsync<BootstrapResponse>("/api/v1/tenancy/bootstrap", Json))!;
        var sale = await Sale(client, boot, 1, PaymentMethod.Card);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CommerceDbContext>();
            var product = await db.Products.SingleAsync(p => p.Id == sale.Lines[0].ProductId);
            product.PriceAmount = 999; product.IsStockTracked = false; product.IsActive = false;
            var tax = await db.TaxCategories.SingleAsync(t => t.Id == product.TaxCategoryId); tax.Rate = .28m;
            var newWarehouse = Guid.NewGuid(); db.Warehouses.Add(new Warehouse { Id = newWarehouse, TenantId = boot.TenantId, LocationId = boot.LocationId, Name = "New warehouse", Code = "NEW" });
            (await db.Locations.SingleAsync(l => l.Id == boot.LocationId)).DefaultSalesWarehouseId = newWarehouse;
            await db.SaveChangesAsync();
        }
        var request = Return(sale, boot.TerminalId, 1, true);
        var quote = await Post<CorrectionQuoteResponse>(client, $"/api/v1/sales/{sale.Id}/returns/quote", request);
        Assert.Equal(10, quote.Total); Assert.Equal(boot.WarehouseId, quote.Lines.Single().WarehouseId);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/v1/sales/{sale.Id}/returns", request with { Refunds = [new(PaymentMethod.Card, 10, null)] }, Json)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/v1/sales/{sale.Id}/returns", request with { Refunds = [new(PaymentMethod.Cash, 10, null)] }, Json)).StatusCode);
        var result = await Post<CorrectionResponse>(client, $"/api/v1/sales/{sale.Id}/returns", request with { Refunds = [new(PaymentMethod.Card, 10, "MANUAL-REFUND-1")] });
        Assert.Equal(sale.Lines[0].TaxableAmount, result.Lines[0].TaxableAmount);
        await using var verify = factory.Services.CreateAsyncScope();
        Assert.Equal(boot.WarehouseId, (await verify.ServiceProvider.GetRequiredService<CommerceDbContext>().InventoryTransactions.SingleAsync(i => i.SourceDocumentId == result.Id)).WarehouseId);
    }

    [Fact]
    public async Task Cash_refund_without_a_drawer_rolls_back_and_completed_void_is_a_separate_full_reversal()
    {
        await using var factory = await PostgresTestAppFactory.CreateAsync(); using var client = await Login(factory);
        var boot = (await client.GetFromJsonAsync<BootstrapResponse>("/api/v1/tenancy/bootstrap", Json))!;
        var sale = await Sale(client, boot, 1);
        var request = new CorrectionRequest(boot.TerminalId, Guid.NewGuid(), "Checkout cancelled", [], [new(PaymentMethod.Cash, 10, null)], SaleStatus.Completed);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/v1/sales/{sale.Id}/void", request, Json)).StatusCode);
        await using (var scope = factory.Services.CreateAsyncScope()) Assert.False(await scope.ServiceProvider.GetRequiredService<CommerceDbContext>().SaleCorrections.AnyAsync());
        await Post<DrawerSessionResponse>(client, $"/api/v1/cash-drawers/terminals/{boot.TerminalId}/open", new OpenDrawerRequest(Guid.NewGuid(), 20));
        var result = await Post<CorrectionResponse>(client, $"/api/v1/sales/{sale.Id}/void", request);
        Assert.Equal("Void", result.Kind); Assert.Equal(10, result.Total);
        Assert.True((await Post<CorrectionResponse>(client, $"/api/v1/sales/{sale.Id}/void", request)).IdempotentReplay);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/v1/sales/{sale.Id}/void", request with { ClientGeneratedId = Guid.NewGuid() }, Json)).StatusCode);
        Assert.Equal("Voided", (await client.GetFromJsonAsync<CorrectionHistoryResponse>($"/api/v1/sales/{sale.Id}/corrections", Json))!.EffectiveStatus);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/v1/sales/{sale.Id}/returns", Return(sale, boot.TerminalId, 1, true) with { Refunds = request.Refunds }, Json)).StatusCode);
    }

    [Fact]
    public async Task Open_cart_void_and_held_completion_compete_without_creating_a_refund_or_double_sale()
    {
        await using var factory = await PostgresTestAppFactory.CreateAsync(); using var client = await Login(factory);
        var boot = (await client.GetFromJsonAsync<BootstrapResponse>("/api/v1/tenancy/bootstrap", Json))!;
        var milk = (await client.GetFromJsonAsync<PagedResponse<ProductResponse>>("/api/v1/products?sku=MILK-1L", Json))!.Items.Single();
        var held = await Post<SaleResponse>(client, "/api/v1/sales/hold", new HoldSaleRequest(boot.LocationId, boot.TerminalId, null, Guid.NewGuid(), [new(milk.Id, 1, 0)]));
        var request = new CorrectionRequest(boot.TerminalId, Guid.NewGuid(), "Cancel saved cart", [], [], SaleStatus.Held);
        var results = await Task.WhenAll(client.PostAsJsonAsync($"/api/v1/sales/{held.Id}/void", request, Json), client.PostAsJsonAsync($"/api/v1/sales/{held.Id}/complete-held", new CompleteHeldSaleRequest([new(PaymentMethod.Cash, 68, 68, null)]), Json));
        Assert.Single(results, r => r.IsSuccessStatusCode); Assert.Single(results, r => r.StatusCode == HttpStatusCode.Conflict);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<CommerceDbContext>();
        var sale = await db.Sales.SingleAsync(s => s.Id == held.Id);
        if (sale.Status == SaleStatus.Voided)
        {
            Assert.Equal("OpenVoid", (await db.SaleCorrections.SingleAsync()).Kind);
            Assert.Empty(await db.SaleRefunds.ToArrayAsync()); Assert.False(await db.InventoryTransactions.AnyAsync(i => i.SourceDocumentId == held.Id));
            Assert.False(await db.Payments.AnyAsync(p => p.SaleId == held.Id));
        }
        else { Assert.Equal(SaleStatus.Completed, sale.Status); Assert.Empty(await db.SaleCorrections.ToArrayAsync()); }
    }

    [Fact]
    public async Task Drawer_open_close_and_movements_are_idempotent_and_cannot_overdraw_or_write_after_close()
    {
        await using var factory = await PostgresTestAppFactory.CreateAsync(); using var client = await Login(factory);
        var boot = (await client.GetFromJsonAsync<BootstrapResponse>("/api/v1/tenancy/bootstrap", Json))!;
        var opening = new OpenDrawerRequest(Guid.NewGuid(), 10);
        var simultaneous = await Task.WhenAll(client.PostAsJsonAsync($"/api/v1/cash-drawers/terminals/{boot.TerminalId}/open", opening, Json), client.PostAsJsonAsync($"/api/v1/cash-drawers/terminals/{boot.TerminalId}/open", opening with { ClientGeneratedId = Guid.NewGuid() }, Json));
        Assert.Single(simultaneous, r => r.IsSuccessStatusCode); Assert.Single(simultaneous, r => r.StatusCode == HttpStatusCode.Conflict);
        var session = (await client.GetFromJsonAsync<DrawerSessionResponse[]>($"/api/v1/cash-drawers/terminals/{boot.TerminalId}", Json))!.Single();
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/v1/cash-drawers/{session.Id}/movements", new DrawerMovementRequest(Guid.NewGuid(), "PaidOut", 11, "Too much"), Json)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/v1/cash-drawers/{session.Id}/movements", new DrawerMovementRequest(Guid.NewGuid(), "Other", 1, "Invalid"), Json)).StatusCode);
        await Post<DrawerSessionResponse>(client, $"/api/v1/cash-drawers/{session.Id}/close", new CloseDrawerRequest(Guid.NewGuid(), 10, null));
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/v1/cash-drawers/{session.Id}/movements", new DrawerMovementRequest(Guid.NewGuid(), "PaidIn", 1, "Late"), Json)).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope(); Assert.Empty(await scope.ServiceProvider.GetRequiredService<CommerceDbContext>().CashDrawerMovements.ToArrayAsync());
    }

    [Fact]
    public async Task Cashier_permissions_and_foreign_sale_terminal_and_drawer_references_are_rejected()
    {
        await using var factory = await PostgresTestAppFactory.CreateAsync(); using var client = await Login(factory); using var cashier = await Login(factory, "cashier@example.test");
        var boot = (await client.GetFromJsonAsync<BootstrapResponse>("/api/v1/tenancy/bootstrap", Json))!;
        var sale = await Sale(client, boot, 1, PaymentMethod.Card);
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.PostAsJsonAsync($"/api/v1/sales/{sale.Id}/returns", Return(sale, boot.TerminalId, 1, true), Json)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.PostAsJsonAsync($"/api/v1/sales/{sale.Id}/void", new CorrectionRequest(boot.TerminalId, Guid.NewGuid(), "Denied", [], [], SaleStatus.Completed), Json)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.GetAsync($"/api/v1/cash-drawers/terminals/{boot.TerminalId}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/v1/sales/{sale.Id}/returns/quote", new { terminalId = boot.TerminalId, clientGeneratedId = Guid.NewGuid(), reason = "Malformed", lines = new object?[] { null }, refunds = Array.Empty<object>() }, Json)).StatusCode);
        var other = Guid.NewGuid(); var terminal = Guid.NewGuid(); var session = Guid.NewGuid();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CommerceDbContext>();
            db.Tenants.Add(new Tenant { Id = other, Name = "Other tenant", Status = Pos.Domain.Tenancy.TenantStatus.Active });
            var organization = Guid.NewGuid(); var business = Guid.NewGuid(); var location = Guid.NewGuid();
            db.Organizations.Add(new Organization { Id = organization, TenantId = other, Name = "Other", LegalName = "Other" });
            db.Businesses.Add(new Business { Id = business, TenantId = other, OrganizationId = organization, Name = "Other" });
            db.Locations.Add(new Location { Id = location, TenantId = other, OrganizationId = organization, BusinessId = business, Name = "Other", Code = "OTHER", StateCode = "19" });
            db.Terminals.Add(new Terminal { Id = terminal, TenantId = other, LocationId = location, Name = "Other", DeviceCode = "OTHER", Status = Pos.Domain.Tenancy.TerminalStatus.Active });
            db.CashDrawerSessions.Add(new CashDrawerSession { Id = session, TenantId = other, TerminalId = terminal, OpenCommandId = Guid.NewGuid(), OpeningFloat = 100, OpenedAtUtc = DateTimeOffset.UtcNow });
            db.Sales.Add(new Pos.Infrastructure.Persistence.Sale { Id = other, TenantId = other, LocationId = location, TerminalId = terminal, InvoiceNumber = "FOREIGN-1", Status = SaleStatus.Completed, ClientGeneratedId = Guid.NewGuid(), Currency = "INR" });
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/sales/{other}/corrections")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync($"/api/v1/sales/{other}/returns/quote", Return(sale, boot.TerminalId, 1, true), Json)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync($"/api/v1/sales/{sale.Id}/returns/quote", Return(sale, terminal, 1, true), Json)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/cash-drawers/terminals/{terminal}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync($"/api/v1/cash-drawers/{session}/close", new CloseDrawerRequest(Guid.NewGuid(), 0, null), Json)).StatusCode);
    }

    [Fact]
    public async Task Drawer_close_and_cash_checkout_have_one_consistent_capture_boundary()
    {
        await using var factory = await PostgresTestAppFactory.CreateAsync(); using var client = await Login(factory);
        var boot = (await client.GetFromJsonAsync<BootstrapResponse>("/api/v1/tenancy/bootstrap", Json))!;
        var session = await Post<DrawerSessionResponse>(client, $"/api/v1/cash-drawers/terminals/{boot.TerminalId}/open", new OpenDrawerRequest(Guid.NewGuid(), 100));
        var milk = (await client.GetFromJsonAsync<PagedResponse<ProductResponse>>("/api/v1/products?sku=MILK-1L", Json))!.Items.Single();
        var saleTask = Post<SaleResponse>(client, "/api/v1/sales/complete", new CompleteSaleRequest(boot.LocationId, boot.TerminalId, null, Guid.NewGuid(), [new(milk.Id, 1, 0)], [new(PaymentMethod.Cash, 68, 100, null)]));
        var closeTask = Post<DrawerSessionResponse>(client, $"/api/v1/cash-drawers/{session.Id}/close", new CloseDrawerRequest(Guid.NewGuid(), 100, "Concurrent close"));
        await Task.WhenAll(saleTask, closeTask);
        var final = (await client.GetFromJsonAsync<DrawerSessionResponse[]>($"/api/v1/cash-drawers/terminals/{boot.TerminalId}", Json))!.Single();
        Assert.NotNull(final.ClosedAtUtc);
        Assert.Equal(100 + final.Movements.Sum(m => m.AmountDelta), final.ExpectedCash);
        Assert.True(final.ExpectedCash is 100 or 168);
        Assert.All(final.Movements, m => Assert.Equal(saleTask.Result.Id, m.SourceId));
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<CommerceDbContext>().Sales.CountAsync());
    }

    [Fact]
    public async Task Concurrent_identical_correction_commands_replay_one_document_and_drawer_debit()
    {
        await using var factory = await PostgresTestAppFactory.CreateAsync(); using var client = await Login(factory);
        var boot = (await client.GetFromJsonAsync<BootstrapResponse>("/api/v1/tenancy/bootstrap", Json))!;
        await Post<DrawerSessionResponse>(client, $"/api/v1/cash-drawers/terminals/{boot.TerminalId}/open", new OpenDrawerRequest(Guid.NewGuid(), 100));
        var sale = await Sale(client, boot, 1);
        var command = Return(sale, boot.TerminalId, 1, true) with { Refunds = [new(PaymentMethod.Cash, 10, null)] };
        var results = await Task.WhenAll(Post<CorrectionResponse>(client, $"/api/v1/sales/{sale.Id}/returns", command), Post<CorrectionResponse>(client, $"/api/v1/sales/{sale.Id}/returns", command));
        Assert.Equal(results[0].Id, results[1].Id); Assert.Single(results, r => r.IdempotentReplay);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<CommerceDbContext>();
        Assert.Equal(1, await db.SaleCorrections.CountAsync()); Assert.Equal(1, await db.CashDrawerMovements.CountAsync(m => m.Kind == "Refund"));
    }

    [Fact]
    public async Task Returning_an_older_invoice_reduces_today_net_without_inventing_today_gross_sales()
    {
        await using var factory = await PostgresTestAppFactory.CreateAsync(); using var client = await Login(factory);
        var boot = (await client.GetFromJsonAsync<BootstrapResponse>("/api/v1/tenancy/bootstrap", Json))!;
        var sale = await Sale(client, boot, 1, PaymentMethod.Card);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CommerceDbContext>();
            (await db.Sales.SingleAsync(s => s.Id == sale.Id)).CompletedAtUtc = DateTimeOffset.UtcNow.AddDays(-2);
            await db.SaveChangesAsync();
        }
        await Post<CorrectionResponse>(client, $"/api/v1/sales/{sale.Id}/returns", Return(sale, boot.TerminalId, 1, false) with { Refunds = [new(PaymentMethod.Card, 10, "OLDER-INVOICE-REFUND")] });
        var report = (await client.GetFromJsonAsync<TodayReportResponse>("/api/v1/reports/today", Json))!;
        Assert.Equal(0, report.GrossRevenue); Assert.Equal(10, report.RefundTotal); Assert.Equal(-10, report.Revenue);
        Assert.Equal(0, report.TransactionCount); Assert.Empty(report.PaymentMix);
        Assert.Equal(10, Assert.Single(report.RefundMix!).Amount);
    }

    private static CorrectionRequest Return(SaleResponse sale, Guid terminal, decimal quantity, bool restock) => new(terminal, Guid.NewGuid(), "Customer return", [new(sale.Lines[0].Id, quantity, restock)], []);
    private static async Task<SaleResponse> Sale(HttpClient client, BootstrapResponse boot, decimal quantity, PaymentMethod method = PaymentMethod.Cash)
    {
        var milk = (await client.GetFromJsonAsync<PagedResponse<ProductResponse>>("/api/v1/products?sku=MILK-1L", Json))!.Items.Single();
        var product = await Post<ProductResponse>(client, "/api/v1/products", new CreateProductRequest(milk.CategoryId, milk.TaxCategoryId, "Return test", $"RET-{Guid.NewGuid():N}", null, "0000", "pc", 10, "INR", true));
        await Post<object>(client, "/api/v1/inventory/opening-stock", new OpeningStockRequest(boot.LocationId, boot.WarehouseId, product.Id, quantity, 5));
        return await Post<SaleResponse>(client, "/api/v1/sales/complete", new CompleteSaleRequest(boot.LocationId, boot.TerminalId, null, Guid.NewGuid(), [new(product.Id, quantity, 0)], [new(method, quantity * 10, method == PaymentMethod.Cash ? quantity * 10 + 20 : quantity * 10, null)]));
    }
    private static async Task<T> Post<T>(HttpClient client, string path, object body)
    { var response = await client.PostAsJsonAsync(path, body, Json); Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync()); return (await response.Content.ReadFromJsonAsync<T>(Json))!; }
    private static async Task<HttpClient> Login(PostgresTestAppFactory factory, string email = "admin@example.test")
    { var client = factory.CreateClient(); var auth = await Post<LoginResponse>(client, "/api/v1/auth/login", new LoginRequest(email, "ChangeMe123!")); client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken); return client; }
    private sealed class DrawerComparer : IEqualityComparer<DrawerSessionResponse>
    { public bool Equals(DrawerSessionResponse? a, DrawerSessionResponse? b) => JsonSerializer.Serialize(a, Json) == JsonSerializer.Serialize(b, Json); public int GetHashCode(DrawerSessionResponse value) => value.Id.GetHashCode(); }
}
