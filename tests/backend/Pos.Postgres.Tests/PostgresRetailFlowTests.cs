using System.Net;
using System.Data.Common;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Pos.Application.Auth;
using Pos.Application.Catalog;
using Pos.Application.Customers;
using Pos.Application.Inventory;
using Pos.Application.Sales;
using Pos.Application.Tenancy;
using Pos.Domain.Inventory;
using Pos.Domain.Sales;
using Pos.Infrastructure.Persistence;
using Xunit;

namespace Pos.Postgres.Tests;

[Collection(PostgresCollection.Name)]
public sealed class PostgresRetailFlowTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public async Task Retail_flow_persists_payments_audit_outbox_and_replays_idempotently()
    {
        await using var factory = await PostgresTestAppFactory.CreateAsync();
        using var client = await AuthenticatedClientAsync(factory);

        var bootstrap = await client.GetFromJsonAsync<BootstrapResponse>("/api/v1/tenancy/bootstrap", JsonOptions);
        var milk = await FindProductAsync(client, "MILK-1L");
        var customerResponse = await client.PostAsJsonAsync("/api/v1/customers", new CreateCustomerRequest("PG Customer", "+919900000001", "pg@example.test", null, "19", 0), JsonOptions);
        Assert.Equal(HttpStatusCode.Created, customerResponse.StatusCode);
        var customer = await customerResponse.Content.ReadFromJsonAsync<CustomerResponse>(JsonOptions);

        var clientGeneratedId = Guid.NewGuid();
        var request = new CompleteSaleRequest(
            bootstrap!.LocationId,
            bootstrap.TerminalId,
            customer!.Id,
            clientGeneratedId,
            [new CompleteSaleLineRequest(milk.Id, 2, 6)],
            [
                new CompleteSalePaymentRequest(PaymentMethod.Cash, 80, 100, "CASH"),
                new CompleteSalePaymentRequest(PaymentMethod.Upi, 50, 50, "UPI")
            ]);

        var response = await client.PostAsJsonAsync("/api/v1/sales/complete", request, JsonOptions);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var sale = await response.Content.ReadFromJsonAsync<SaleResponse>(JsonOptions);
        Assert.NotNull(sale);
        Assert.Equal(130m, sale!.GrandTotal);
        Assert.Equal(20m, sale.Payments.Single(p => p.Method == PaymentMethod.Cash).ChangeDue);

        var replayResponse = await client.PostAsJsonAsync("/api/v1/sales/complete", request, JsonOptions);
        var replay = await replayResponse.Content.ReadFromJsonAsync<SaleResponse>(JsonOptions);
        Assert.Equal(HttpStatusCode.Created, replayResponse.StatusCode);
        Assert.True(replay!.IdempotentReplay);
        Assert.Equal(sale.Id, replay.Id);
        Assert.Equal(sale.InvoiceNumber, replay.InvoiceNumber);
        var balancesResponse = await client.GetAsync("/api/v1/inventory/balances");
        Assert.True(balancesResponse.IsSuccessStatusCode, await balancesResponse.Content.ReadAsStringAsync());
        var reportResponse = await client.GetAsync("/api/v1/reports/today");
        Assert.True(reportResponse.IsSuccessStatusCode, await reportResponse.Content.ReadAsStringAsync());
        var historyResponse = await client.GetAsync($"/api/v1/sales/history?invoice={sale.InvoiceNumber}&method=Cash&pageSize=1");
        Assert.True(historyResponse.IsSuccessStatusCode, await historyResponse.Content.ReadAsStringAsync());
        Assert.NotNull(sale.Receipt);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CommerceDbContext>();
        Assert.Equal(1, await db.Sales.CountAsync(s => s.ClientGeneratedId == clientGeneratedId));
        Assert.Equal(2, await db.Payments.CountAsync(p => p.SaleId == sale.Id));
        Assert.True(await db.AuditLogs.AnyAsync(a => a.EntityId == sale.Id && a.Action == "SaleCompleted"));
        Assert.True(await db.OutboxEvents.AnyAsync(e => e.EventType == "SaleCompleted"));

        await using var restartScope = factory.Services.CreateAsyncScope();
        var restartedDb = restartScope.ServiceProvider.GetRequiredService<CommerceDbContext>();
        Assert.True(await restartedDb.Sales.AsNoTracking().AnyAsync(s => s.Id == sale.Id));
    }

    [Fact]
    public async Task Invalid_reference_and_oversell_roll_back_without_partial_rows()
    {
        await using var factory = await PostgresTestAppFactory.CreateAsync();
        using var client = await AuthenticatedClientAsync(factory);

        var bootstrap = await client.GetFromJsonAsync<BootstrapResponse>("/api/v1/tenancy/bootstrap", JsonOptions);
        var milk = await FindProductAsync(client, "MILK-1L");

        var invalid = await client.PostAsJsonAsync("/api/v1/sales/complete", new CompleteSaleRequest(
            Guid.NewGuid(),
            bootstrap!.TerminalId,
            null,
            Guid.NewGuid(),
            [new CompleteSaleLineRequest(milk.Id, 1, 0)],
            [new CompleteSalePaymentRequest(PaymentMethod.Cash, 68, 68, null)]), JsonOptions);
        Assert.Equal(HttpStatusCode.NotFound, invalid.StatusCode);

        var oversellClientId = Guid.NewGuid();
        var oversell = await client.PostAsJsonAsync("/api/v1/sales/complete", new CompleteSaleRequest(
            bootstrap!.LocationId,
            bootstrap.TerminalId,
            null,
            oversellClientId,
            [new CompleteSaleLineRequest(milk.Id, 9999, 0)],
            [new CompleteSalePaymentRequest(PaymentMethod.Cash, 679932, 679932, null)]), JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, oversell.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CommerceDbContext>();
        Assert.False(await db.Sales.AnyAsync(s => s.ClientGeneratedId == oversellClientId));
        Assert.False(await db.SaleLines.AnyAsync());
        Assert.False(await db.Payments.AnyAsync());
        Assert.False(await db.InventoryTransactions.AnyAsync(t => t.SourceDocumentType == "Sale"));
        Assert.False(await db.InvoiceSequences.AnyAsync());
        Assert.False(await db.AuditLogs.AnyAsync(a => a.EntityName == "Sale"));
        Assert.False(await db.OutboxEvents.AnyAsync(e => e.EventType == "SaleCompleted"));
    }

    [Fact]
    public async Task Cashier_discount_threshold_is_enforced_server_side()
    {
        await using var factory = await PostgresTestAppFactory.CreateAsync();
        using var admin = await AuthenticatedClientAsync(factory);
        using var cashier = await AuthenticatedClientAsync(factory, "cashier@example.test");

        var bootstrap = await admin.GetFromJsonAsync<BootstrapResponse>("/api/v1/tenancy/bootstrap", JsonOptions);
        var milk = await FindProductAsync(admin, "MILK-1L");
        var tooLargeDiscount = new CompleteSaleRequest(
            bootstrap!.LocationId,
            bootstrap.TerminalId,
            null,
            Guid.NewGuid(),
            [new CompleteSaleLineRequest(milk.Id, 1, 20)],
            [new CompleteSalePaymentRequest(PaymentMethod.Cash, 48, 48, null)]);

        var cashierResponse = await cashier.PostAsJsonAsync("/api/v1/sales/complete", tooLargeDiscount, JsonOptions);
        Assert.Equal(HttpStatusCode.Forbidden, cashierResponse.StatusCode);

        var adminResponse = await admin.PostAsJsonAsync("/api/v1/sales/complete", tooLargeDiscount with { ClientGeneratedId = Guid.NewGuid() }, JsonOptions);
        Assert.Equal(HttpStatusCode.Created, adminResponse.StatusCode);
    }

    [Fact]
    public async Task Parallel_final_unit_sale_allows_exactly_one_completion()
    {
        await using var factory = await PostgresTestAppFactory.CreateAsync();
        using var client = await AuthenticatedClientAsync(factory);
        var bootstrap = await client.GetFromJsonAsync<BootstrapResponse>("/api/v1/tenancy/bootstrap", JsonOptions);
        var product = await CreateProductWithStockAsync(client, bootstrap!, "LAST", 10, 1);
        var secondTerminalId = Guid.NewGuid();
        await using (var terminalScope = factory.Services.CreateAsyncScope())
        {
            var terminalDb = terminalScope.ServiceProvider.GetRequiredService<CommerceDbContext>();
            terminalDb.Terminals.Add(new Pos.Infrastructure.Persistence.Terminal { Id = secondTerminalId, TenantId = DevelopmentSeeder.DemoTenantId, LocationId = bootstrap!.LocationId, Name = "Second counter", DeviceCode = "SECOND", Status = Pos.Domain.Tenancy.TerminalStatus.Active });
            await terminalDb.SaveChangesAsync();
        }

        async Task<(HttpStatusCode Status, string Body)> SellAsync(Guid terminalId)
        {
            using var saleClient = await AuthenticatedClientAsync(factory);
            var response = await saleClient.PostAsJsonAsync("/api/v1/sales/complete", new CompleteSaleRequest(
                bootstrap!.LocationId,
                terminalId,
                null,
                Guid.NewGuid(),
                [new CompleteSaleLineRequest(product.Id, 1, 0)],
                [new CompleteSalePaymentRequest(PaymentMethod.Cash, 10, 10, null)]), JsonOptions);
            return (response.StatusCode, await response.Content.ReadAsStringAsync());
        }

        var results = await Task.WhenAll(SellAsync(bootstrap!.TerminalId), SellAsync(secondTerminalId));
        Assert.Equal(1, results.Count(x => x.Status == HttpStatusCode.Created));
        Assert.Equal(1, results.Count(x => x.Status == HttpStatusCode.Conflict));

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CommerceDbContext>();
        var balance = await db.InventoryTransactions.Where(t => t.ProductId == product.Id).SumAsync(t => t.QuantityDelta);
        var saleIds = await db.SaleLines.Where(l => l.ProductId == product.Id).Select(l => l.SaleId).ToArrayAsync();
        Assert.Equal(0, balance);
        Assert.Equal(1, await db.Sales.CountAsync(s => saleIds.Contains(s.Id)));
    }

    [Fact]
    public async Task Parallel_invoice_allocation_stays_unique_and_sequence_survives_restart()
    {
        await using var factory = await PostgresTestAppFactory.CreateAsync();
        using var client = await AuthenticatedClientAsync(factory);
        var bootstrap = await client.GetFromJsonAsync<BootstrapResponse>("/api/v1/tenancy/bootstrap", JsonOptions);
        var products = new List<ProductResponse>();
        for (var i = 0; i < 8; i++)
            products.Add(await CreateProductWithStockAsync(client, bootstrap!, $"INVOICE-{i}", 12, 2));

        async Task<SaleResponse> SellAsync(int index)
        {
            using var saleClient = await AuthenticatedClientAsync(factory);
            var response = await saleClient.PostAsJsonAsync("/api/v1/sales/complete", new CompleteSaleRequest(
                bootstrap!.LocationId,
                bootstrap.TerminalId,
                null,
                Guid.NewGuid(),
                [new CompleteSaleLineRequest(products[index].Id, 1, 0)],
                [new CompleteSalePaymentRequest(PaymentMethod.Cash, 12, 12, null)]), JsonOptions);
            Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
            return (await response.Content.ReadFromJsonAsync<SaleResponse>(JsonOptions))!;
        }

        var sales = await Task.WhenAll(Enumerable.Range(0, 8).Select(SellAsync));
        Assert.Equal(8, sales.Select(s => s.InvoiceNumber).Distinct(StringComparer.Ordinal).Count());

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CommerceDbContext>();
        Assert.True(await db.InvoiceSequences.AsNoTracking().AnyAsync(s => s.NextNumber >= 9));
        await using var restartedFactory = factory.Reopen();
        using var restartedClient = await AuthenticatedClientAsync(restartedFactory);
        var persisted = await restartedClient.GetFromJsonAsync<PagedResponse<SaleResponse>>("/api/v1/sales/history?pageSize=20", JsonOptions);
        Assert.Equal(8, persisted!.TotalCount);
        var next = await restartedClient.PostAsJsonAsync("/api/v1/sales/complete", new CompleteSaleRequest(
            bootstrap!.LocationId, bootstrap.TerminalId, null, Guid.NewGuid(), [new CompleteSaleLineRequest(products[0].Id, 1, 0)],
            [new CompleteSalePaymentRequest(PaymentMethod.Cash, 12, 12, null)]), JsonOptions);
        next.EnsureSuccessStatusCode();
        Assert.EndsWith("-0009", (await next.Content.ReadFromJsonAsync<SaleResponse>(JsonOptions))!.InvoiceNumber);
    }

    [Fact]
    public async Task Held_sale_can_resume_and_complete_without_early_inventory_deduction()
    {
        await using var factory = await PostgresTestAppFactory.CreateAsync();
        using var client = await AuthenticatedClientAsync(factory);
        var bootstrap = await client.GetFromJsonAsync<BootstrapResponse>("/api/v1/tenancy/bootstrap", JsonOptions);
        var product = await CreateProductWithStockAsync(client, bootstrap!, "HELD", 20, 2);

        var hold = await client.PostAsJsonAsync("/api/v1/sales/hold", new HoldSaleRequest(
            bootstrap!.LocationId,
            bootstrap.TerminalId,
            null,
            Guid.NewGuid(),
            [new CompleteSaleLineRequest(product.Id, 1, 0)]), JsonOptions);
        Assert.Equal(HttpStatusCode.Created, hold.StatusCode);
        var held = await hold.Content.ReadFromJsonAsync<SaleResponse>(JsonOptions);
        Assert.Equal(SaleStatus.Held, held!.Status);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CommerceDbContext>();
            Assert.Equal(2, await db.InventoryTransactions.Where(t => t.ProductId == product.Id).SumAsync(t => t.QuantityDelta));
            Assert.False(await db.Payments.AnyAsync(p => p.SaleId == held.Id));
        }

        var completedResponse = await client.PostAsJsonAsync($"/api/v1/sales/{held.Id}/complete-held", new CompleteHeldSaleRequest(
            [new CompleteSalePaymentRequest(PaymentMethod.Cash, 20, 25, "HELD-CASH")]), JsonOptions);
        Assert.Equal(HttpStatusCode.Created, completedResponse.StatusCode);
        var completed = await completedResponse.Content.ReadFromJsonAsync<SaleResponse>(JsonOptions);
        Assert.Equal(SaleStatus.Completed, completed!.Status);
        Assert.StartsWith("KAL-", completed.InvoiceNumber, StringComparison.Ordinal);
        Assert.Equal(5, completed.Payments.Single().ChangeDue);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CommerceDbContext>();
            Assert.Equal(1, await db.InventoryTransactions.Where(t => t.ProductId == product.Id).SumAsync(t => t.QuantityDelta));
            Assert.True(await db.AuditLogs.AnyAsync(a => a.EntityId == held.Id && a.Action == "HeldSaleCompleted"));
        }
    }

    [Fact]
    public async Task Concurrent_stock_decrease_and_checkout_cannot_both_consume_final_unit()
    {
        await using var factory = await PostgresTestAppFactory.CreateAsync();
        using var client = await AuthenticatedClientAsync(factory);
        var boot = (await client.GetFromJsonAsync<BootstrapResponse>("/api/v1/tenancy/bootstrap", JsonOptions))!;
        var product = await CreateProductWithStockAsync(client, boot, "ADJUST", 10, 1);
        var sale = client.PostAsJsonAsync("/api/v1/sales/complete", new CompleteSaleRequest(
            boot.LocationId, boot.TerminalId, null, Guid.NewGuid(),
            [new CompleteSaleLineRequest(product.Id, 1, 0)],
            [new CompleteSalePaymentRequest(PaymentMethod.Cash, 10, 10, null)]), JsonOptions);
        var adjustment = client.PostAsJsonAsync("/api/v1/inventory/adjustments", new StockAdjustmentRequest(
            boot.LocationId, boot.WarehouseId, product.Id, 1, null, InventoryTransactionType.Damage, "Broken"), JsonOptions);
        var results = await Task.WhenAll(sale, adjustment);
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.Created);
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.Conflict);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CommerceDbContext>();
        Assert.Equal(0m, await db.InventoryTransactions.Where(t => t.ProductId == product.Id).SumAsync(t => t.QuantityDelta));
    }

    [Fact]
    public async Task Checkout_snapshot_taken_before_a_committed_adjustment_cannot_consume_old_stock()
    {
        await using var factory = await PostgresTestAppFactory.CreateAsync();
        var pause = new PauseProductRead();
        factory.CommandInterceptor = pause;
        using var client = await AuthenticatedClientAsync(factory);
        var boot = (await client.GetFromJsonAsync<BootstrapResponse>("/api/v1/tenancy/bootstrap", JsonOptions))!;
        var product = await CreateProductWithStockAsync(client, boot, "SNAPSHOT", 10, 1);
        pause.Armed = true;
        var sale = client.PostAsJsonAsync("/api/v1/sales/complete", new CompleteSaleRequest(
            boot.LocationId, boot.TerminalId, null, Guid.NewGuid(),
            [new CompleteSaleLineRequest(product.Id, 1, 0)], [new CompleteSalePaymentRequest(PaymentMethod.Cash, 10, 10, null)]), JsonOptions);
        try
        {
            // Checkout already has a serializable snapshot, but has not acquired its stock lock.
            await pause.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
            var adjustment = await client.PostAsJsonAsync("/api/v1/inventory/adjustments", new StockAdjustmentRequest(
                boot.LocationId, boot.WarehouseId, product.Id, 1, null, InventoryTransactionType.Damage, "Snapshot race"), JsonOptions);
            Assert.Equal(HttpStatusCode.Created, adjustment.StatusCode);
        }
        finally { pause.Release.TrySetResult(); }
        Assert.Equal(HttpStatusCode.Conflict, (await sale.WaitAsync(TimeSpan.FromSeconds(30))).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CommerceDbContext>();
        Assert.Equal(0m, await db.InventoryTransactions.Where(t => t.ProductId == product.Id).SumAsync(t => t.QuantityDelta));
        Assert.False(await db.Sales.AnyAsync());
        Assert.False(await db.OutboxEvents.AnyAsync(e => e.EventType == "SaleCompleted"));
    }

    private sealed class PauseProductRead : DbCommandInterceptor
    {
        private int _paused;
        public bool Armed { get; set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (Armed && command.CommandText.Contains("FROM products", StringComparison.Ordinal) && Interlocked.CompareExchange(ref _paused, 1, 0) == 0)
            {
                Entered.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }

    [Fact]
    public async Task Held_cart_edits_preserve_sale_identity_and_apply_only_final_quantities()
    {
        await using var factory = await PostgresTestAppFactory.CreateAsync();
        using var client = await AuthenticatedClientAsync(factory);
        var boot = (await client.GetFromJsonAsync<BootstrapResponse>("/api/v1/tenancy/bootstrap", JsonOptions))!;
        var product = await CreateProductWithStockAsync(client, boot, "EDIT", 10, 5);
        var cart = new HoldSaleRequest(boot.LocationId, boot.TerminalId, null, Guid.NewGuid(), [new CompleteSaleLineRequest(product.Id, 1, 0)]);
        var holdResponse = await client.PostAsJsonAsync("/api/v1/sales/hold", cart, JsonOptions);
        holdResponse.EnsureSuccessStatusCode();
        var held = (await holdResponse.Content.ReadFromJsonAsync<SaleResponse>(JsonOptions))!;
        var update = await client.PutAsJsonAsync($"/api/v1/sales/{held.Id}/cart", cart with { Lines = [new CompleteSaleLineRequest(product.Id, 2, 1)] }, JsonOptions);
        Assert.True(update.IsSuccessStatusCode, await update.Content.ReadAsStringAsync());
        var edited = (await update.Content.ReadFromJsonAsync<SaleResponse>(JsonOptions))!;
        Assert.Equal(held.Id, edited.Id);
        Assert.Equal(19m, edited.GrandTotal);
        var complete = await client.PostAsJsonAsync($"/api/v1/sales/{held.Id}/complete-held",
            new CompleteHeldSaleRequest([new CompleteSalePaymentRequest(PaymentMethod.Cash, 19, 20, null)]), JsonOptions);
        complete.EnsureSuccessStatusCode();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CommerceDbContext>();
        Assert.Equal(1, await db.Sales.CountAsync(s => s.ClientGeneratedId == cart.ClientGeneratedId));
        Assert.Equal(3m, await db.InventoryTransactions.Where(t => t.ProductId == product.Id).SumAsync(t => t.QuantityDelta));
        Assert.Equal(2m, (await db.SaleLines.SingleAsync(l => l.SaleId == held.Id)).Quantity);
    }

    [Fact]
    public async Task Concurrent_held_completion_captures_only_once()
    {
        await using var factory = await PostgresTestAppFactory.CreateAsync();
        using var client = await AuthenticatedClientAsync(factory);
        var boot = (await client.GetFromJsonAsync<BootstrapResponse>("/api/v1/tenancy/bootstrap", JsonOptions))!;
        var product = await CreateProductWithStockAsync(client, boot, "ONCE", 10, 3);
        var hold = await client.PostAsJsonAsync("/api/v1/sales/hold", new HoldSaleRequest(
            boot.LocationId, boot.TerminalId, null, Guid.NewGuid(), [new CompleteSaleLineRequest(product.Id, 1, 0)]), JsonOptions);
        hold.EnsureSuccessStatusCode();
        var held = (await hold.Content.ReadFromJsonAsync<SaleResponse>(JsonOptions))!;
        var request = new CompleteHeldSaleRequest([new CompleteSalePaymentRequest(PaymentMethod.Cash, 10, 10, null)]);
        var responses = await Task.WhenAll(
            client.PostAsJsonAsync($"/api/v1/sales/{held.Id}/complete-held", request, JsonOptions),
            client.PostAsJsonAsync($"/api/v1/sales/{held.Id}/complete-held", request, JsonOptions));
        foreach (var response in responses) Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CommerceDbContext>();
        Assert.Equal(1, await db.Payments.CountAsync(p => p.SaleId == held.Id));
        Assert.Equal(2m, await db.InventoryTransactions.Where(t => t.ProductId == product.Id).SumAsync(t => t.QuantityDelta));
    }

    [Fact]
    public async Task Tenant_scoped_reads_and_catalog_references_reject_other_tenant_data()
    {
        await using var factory = await PostgresTestAppFactory.CreateAsync();
        using var admin = await AuthenticatedClientAsync(factory);
        var tenantId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var taxId = Guid.NewGuid();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CommerceDbContext>();
            db.Tenants.Add(new Pos.Infrastructure.Persistence.Tenant { Id = tenantId, Name = "Tenant B", Status = Pos.Domain.Tenancy.TenantStatus.Active });
            db.Users.Add(new Pos.Infrastructure.Persistence.User { Id = Guid.NewGuid(), TenantId = tenantId, DisplayName = "Tenant B User", Email = "tenant-b@example.test", PasswordHash = scope.ServiceProvider.GetRequiredService<Pos.Domain.Security.IPasswordHasher>().Hash("ChangeMe123!") });
            db.Categories.Add(new Pos.Infrastructure.Persistence.Category { Id = categoryId, TenantId = tenantId, Name = "B Category" });
            db.TaxCategories.Add(new Pos.Infrastructure.Persistence.TaxCategory { Id = taxId, TenantId = tenantId, Name = "B Tax", CountryCode = "IN", Rate = 0, TaxBehavior = Pos.Domain.Catalog.TaxBehavior.Inclusive });
            db.Products.Add(new Pos.Infrastructure.Persistence.Product { Id = Guid.NewGuid(), TenantId = tenantId, CategoryId = categoryId, TaxCategoryId = taxId, Name = "Private B Product", Sku = "PRIVATE-B", UnitOfMeasure = "pc", PriceAmount = 10 });
            await db.SaveChangesAsync();
        }
        using var other = await AuthenticatedClientAsync(factory, "tenant-b@example.test");
        var bProducts = await other.GetFromJsonAsync<PagedResponse<ProductResponse>>("/api/v1/products", JsonOptions);
        Assert.Equal("PRIVATE-B", Assert.Single(bProducts!.Items).Sku);
        var aProducts = await admin.GetFromJsonAsync<PagedResponse<ProductResponse>>("/api/v1/products?sku=PRIVATE-B", JsonOptions);
        Assert.Empty(aProducts!.Items);
        var invalid = await admin.PostAsJsonAsync("/api/v1/products", new CreateProductRequest(categoryId, taxId, "Cross tenant", "CROSS", null, null, "pc", 10, "INR", true), JsonOptions);
        Assert.Equal(HttpStatusCode.NotFound, invalid.StatusCode);
        var ownProduct = await FindProductAsync(admin, "MILK-1L");
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CommerceDbContext>();
            var exception = await Assert.ThrowsAsync<PostgresException>(() =>
                db.Database.ExecuteSqlInterpolatedAsync($"UPDATE products SET \"CategoryId\" = {categoryId} WHERE \"Id\" = {ownProduct.Id}"));
            Assert.Equal("23503", exception.SqlState);
        }
        var forbidden = await other.PostAsJsonAsync("/api/v1/products", new CreateProductRequest(categoryId, taxId, "Forbidden", "FORBIDDEN", null, null, "pc", 10, "INR", true), JsonOptions);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    [Fact]
    public async Task Product_lifecycle_does_not_rebuild_interstate_invoice_snapshots()
    {
        await using var factory = await PostgresTestAppFactory.CreateAsync();
        using var client = await AuthenticatedClientAsync(factory);
        var boot = (await client.GetFromJsonAsync<BootstrapResponse>("/api/v1/tenancy/bootstrap", JsonOptions))!;
        var product = await CreateProductWithStockAsync(client, boot, "SNAPSHOT", 105, 3);
        var customerResponse = await client.PostAsJsonAsync("/api/v1/customers",
            new CreateCustomerRequest("Interstate buyer", null, null, "19ABCDE1234F1Z5", "27", 0), JsonOptions);
        customerResponse.EnsureSuccessStatusCode();
        var customer = (await customerResponse.Content.ReadFromJsonAsync<CustomerResponse>(JsonOptions))!;
        var complete = await client.PostAsJsonAsync("/api/v1/sales/complete",
            new CompleteSaleRequest(boot.LocationId, boot.TerminalId, customer.Id, Guid.NewGuid(),
                [new CompleteSaleLineRequest(product.Id, 1, 0)],
                [new CompleteSalePaymentRequest(PaymentMethod.Upi, 105, 105, "manual")]), JsonOptions);
        complete.EnsureSuccessStatusCode();
        var sale = (await complete.Content.ReadFromJsonAsync<SaleResponse>(JsonOptions))!;
        Assert.Equal(5m, sale.Lines.Single().IgstAmount);
        Assert.Equal(0m, sale.Lines.Single().CgstAmount);
        var update = await client.PutAsJsonAsync($"/api/v1/products/{product.Id}",
            new UpdateProductRequest(product.CategoryId, product.TaxCategoryId, "Renamed product", product.Sku, product.Barcode, "9999", product.UnitOfMeasure, 210, "INR", true, true), JsonOptions);
        update.EnsureSuccessStatusCode();
        (await client.PostAsync($"/api/v1/products/{product.Id}/deactivate", null)).EnsureSuccessStatusCode();
        var hidden = await client.GetFromJsonAsync<PagedResponse<ProductResponse>>($"/api/v1/products?sku={product.Sku}", JsonOptions);
        Assert.Empty(hidden!.Items);
        (await client.PostAsync($"/api/v1/products/{product.Id}/activate", null)).EnsureSuccessStatusCode();
        var history = await client.GetFromJsonAsync<PagedResponse<SaleResponse>>($"/api/v1/sales/history?invoice={sale.InvoiceNumber}&customerId={customer.Id}&terminalId={boot.TerminalId}", JsonOptions);
        var persisted = Assert.Single(history!.Items);
        Assert.Equal(product.Name, persisted.Lines.Single().ProductName);
        Assert.Equal("0000", persisted.Lines.Single().HsnSac);
        Assert.Equal(105m, persisted.Lines.Single().UnitPrice);
        Assert.Equal("Interstate buyer", persisted.Receipt!.CustomerName);
    }

    private static async Task<HttpClient> AuthenticatedClientAsync(PostgresTestAppFactory factory, string email = "admin@example.test")
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, "ChangeMe123!"), JsonOptions);
        response.EnsureSuccessStatusCode();
        var login = await response.Content.ReadFromJsonAsync<TestLoginResponse>(JsonOptions);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.AccessToken);
        return client;
    }

    private static async Task<ProductResponse> FindProductAsync(HttpClient client, string sku)
    {
        var page = await client.GetFromJsonAsync<PagedResponse<ProductResponse>>($"/api/v1/products?sku={sku}&pageSize=1", JsonOptions);
        return Assert.Single(page!.Items);
    }

    private static async Task<ProductResponse> CreateProductWithStockAsync(HttpClient client, BootstrapResponse bootstrap, string prefix, decimal price, decimal quantity)
    {
        var milk = await FindProductAsync(client, "MILK-1L");
        var productResponse = await client.PostAsJsonAsync("/api/v1/products", new CreateProductRequest(
            milk.CategoryId,
            milk.TaxCategoryId,
            $"{prefix} Product {Guid.NewGuid():N}",
            $"{prefix}-{Guid.NewGuid():N}",
            null,
            "0000",
            "pc",
            price,
            "INR",
            true), JsonOptions);
        productResponse.EnsureSuccessStatusCode();
        var product = await productResponse.Content.ReadFromJsonAsync<ProductResponse>(JsonOptions);

        var stockResponse = await client.PostAsJsonAsync("/api/v1/inventory/opening-stock", new OpeningStockRequest(bootstrap.LocationId, bootstrap.WarehouseId, product!.Id, quantity, price * 0.7m), JsonOptions);
        stockResponse.EnsureSuccessStatusCode();
        return product;
    }

    private sealed record TestLoginResponse(string AccessToken);
    private sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);
}

public sealed class PostgresTestAppFactory : WebApplicationFactory<Program>
{
    public DbCommandInterceptor? CommandInterceptor { get; set; }
    private readonly string _adminConnectionString;
    private readonly string _databaseName;
    private readonly string _appConnectionString;
    private bool _ownsDatabase = true;

    public PostgresTestAppFactory Reopen() => new(_adminConnectionString, _databaseName, _appConnectionString) { _ownsDatabase = false };

    private PostgresTestAppFactory(string adminConnectionString, string databaseName, string appConnectionString)
    {
        _adminConnectionString = adminConnectionString;
        _databaseName = databaseName;
        _appConnectionString = appConnectionString;
    }

    public static async Task<PostgresTestAppFactory> CreateAsync()
    {
        var admin = Environment.GetEnvironmentVariable("POSTGRES_TEST_ADMIN_CONNECTION_STRING")
            ?? "Host=localhost;Port=5432;Database=postgres;Username=postgres";
        var databaseName = $"pilot_m2_{Guid.NewGuid():N}";
        var appBuilder = new NpgsqlConnectionStringBuilder(admin) { Database = databaseName };

        await using var connection = new NpgsqlConnection(admin);
        await connection.OpenAsync();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = $"CREATE DATABASE \"{databaseName}\"";
            await command.ExecuteNonQueryAsync();
        }

        return new PostgresTestAppFactory(admin, databaseName, appBuilder.ConnectionString);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration(configuration =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = _appConnectionString,
                ["AUTH_SIGNING_KEY"] = "postgres-testing-signing-key"
            });
        });
        builder.ConfigureServices(services =>
        {
            var descriptors = services.Where(d => d.ServiceType == typeof(DbContextOptions) ||
                d.ServiceType == typeof(DbContextOptions<CommerceDbContext>) ||
                d.ServiceType.FullName?.Contains("IDbContextOptionsConfiguration", StringComparison.Ordinal) == true).ToArray();
            foreach (var descriptor in descriptors) services.Remove(descriptor);
            services.AddDbContext<CommerceDbContext>(options =>
            {
                options.UseNpgsql(_appConnectionString);
                if (CommandInterceptor is not null) options.AddInterceptors(CommandInterceptor);
            });
            using var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CommerceDbContext>();
            db.Database.Migrate();
            var seeder = scope.ServiceProvider.GetRequiredService<DevelopmentSeeder>();
            seeder.SeedAsync(CancellationToken.None).GetAwaiter().GetResult();
        });
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        if (!_ownsDatabase) return;
        NpgsqlConnection.ClearAllPools();
        await using var connection = new NpgsqlConnection(_adminConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)";
        await command.ExecuteNonQueryAsync();
    }
}
