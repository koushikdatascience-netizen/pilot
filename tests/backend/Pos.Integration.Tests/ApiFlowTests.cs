using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pos.Application.Auth;
using Pos.Application.Catalog;
using Pos.Application.Customers;
using Pos.Application.Inventory;
using Pos.Application.Sales;
using Pos.Application.Tenancy;
using Pos.Domain.Sales;
using Pos.Infrastructure.Persistence;

namespace Pos.Integration.Tests;

public sealed class ApiFlowTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public async Task AuthenticatedRetailFlowPersistsAuditOutboxInventoryAndIdempotency()
    {
        await using var factory = new TestAppFactory();
        using var client = factory.CreateClient();
        var token = await LoginAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var bootstrap = await client.GetFromJsonAsync<BootstrapResponse>("/api/v1/tenancy/bootstrap");
        Assert.NotNull(bootstrap);

        var products = await client.GetFromJsonAsync<PagedResponse<ProductResponse>>("/api/v1/products");
        var milk = Assert.Single(products!.Items, p => p.Sku == "MILK-1L");

        var createdProductResponse = await client.PostAsJsonAsync("/api/v1/products", new CreateProductRequest(
            milk.CategoryId,
            milk.TaxCategoryId,
            "Test Biscuit",
            $"BIS-{Guid.NewGuid():N}",
            "8901999999999",
            "1905",
            "pc",
            25,
            "INR",
            true));
        Assert.Equal(HttpStatusCode.Created, createdProductResponse.StatusCode);

        var customerResponse = await client.PostAsJsonAsync("/api/v1/customers", new CreateCustomerRequest("Test Customer", "+919999999999", "test@example.test", null, "19", 0));
        Assert.Equal(HttpStatusCode.Created, customerResponse.StatusCode);
        var customer = await customerResponse.Content.ReadFromJsonAsync<CustomerResponse>();

        var openingResponse = await client.PostAsJsonAsync("/api/v1/inventory/opening-stock", new OpeningStockRequest(bootstrap!.LocationId, bootstrap.WarehouseId, milk.Id, 5, 40));
        Assert.Equal(HttpStatusCode.Created, openingResponse.StatusCode);

        var balancesBefore = await client.GetFromJsonAsync<StockBalanceResponse[]>("/api/v1/inventory/balances");
        var milkBefore = balancesBefore!.Single(b => b.ProductId == milk.Id).QuantityOnHand;

        var clientGeneratedId = Guid.NewGuid();
        var saleRequest = new CompleteSaleRequest(
            bootstrap.LocationId,
            bootstrap.TerminalId,
            customer!.Id,
            clientGeneratedId,
            [new CompleteSaleLineRequest(milk.Id, 2, 6)],
            [new CompleteSalePaymentRequest(PaymentMethod.Cash, 130, 150, "TEST")]);

        var saleResponse = await client.PostAsJsonAsync("/api/v1/sales/complete", saleRequest);
        Assert.Equal(HttpStatusCode.Created, saleResponse.StatusCode);
        var sale = await saleResponse.Content.ReadFromJsonAsync<SaleResponse>(JsonOptions);
        Assert.NotNull(sale);
        Assert.Equal(130, sale!.GrandTotal);
        Assert.Equal(6.19m, sale.TaxTotal);
        Assert.Equal(3.10m, sale.Lines.Single().CgstAmount);
        Assert.Equal(3.09m, sale.Lines.Single().SgstAmount);
        Assert.NotEqual("", sale.InvoiceNumber);

        var replayResponse = await client.PostAsJsonAsync("/api/v1/sales/complete", saleRequest);
        var replay = await replayResponse.Content.ReadFromJsonAsync<SaleResponse>(JsonOptions);
        Assert.Equal(HttpStatusCode.Created, replayResponse.StatusCode);
        Assert.True(replay!.IdempotentReplay);
        Assert.Equal(sale.Id, replay.Id);

        var balancesAfter = await client.GetFromJsonAsync<StockBalanceResponse[]>("/api/v1/inventory/balances");
        var milkAfter = balancesAfter!.Single(b => b.ProductId == milk.Id).QuantityOnHand;
        Assert.Equal(milkBefore - 2, milkAfter);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CommerceDbContext>();
        Assert.Equal(1, await db.Sales.CountAsync(s => s.ClientGeneratedId == clientGeneratedId));
        Assert.True(await db.AuditLogs.AnyAsync(a => a.EntityId == sale.Id && a.Action == "SaleCompleted"));
        Assert.True(await db.OutboxEvents.AnyAsync(e => e.EventType == "SaleCompleted"));
        Assert.True(await db.OutboxEvents.AnyAsync(e => e.EventType == "PaymentRecorded"));
        Assert.True(await db.OutboxEvents.AnyAsync(e => e.EventType == "InventoryReduced"));
    }

    [Fact]
    public async Task InvalidTenantReferenceAndInsufficientInventoryRejectWithoutPartialSale()
    {
        await using var factory = new TestAppFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await LoginAsync(client));

        var bootstrap = await client.GetFromJsonAsync<BootstrapResponse>("/api/v1/tenancy/bootstrap");
        var products = await client.GetFromJsonAsync<PagedResponse<ProductResponse>>("/api/v1/products");
        var milk = products!.Items.Single(p => p.Sku == "MILK-1L");

        var invalidLocationSale = new CompleteSaleRequest(
            Guid.NewGuid(),
            bootstrap!.TerminalId,
            null,
            Guid.NewGuid(),
            [new CompleteSaleLineRequest(milk.Id, 1, 0)],
            [new CompleteSalePaymentRequest(PaymentMethod.Cash, 68, 68, null)]);
        var invalidResponse = await client.PostAsJsonAsync("/api/v1/sales/complete", invalidLocationSale);
        Assert.Equal(HttpStatusCode.NotFound, invalidResponse.StatusCode);

        var oversell = new CompleteSaleRequest(
            bootstrap.LocationId,
            bootstrap.TerminalId,
            null,
            Guid.NewGuid(),
            [new CompleteSaleLineRequest(milk.Id, 1000, 0)],
            [new CompleteSalePaymentRequest(PaymentMethod.Cash, 68000, 68000, null)]);
        var oversellResponse = await client.PostAsJsonAsync("/api/v1/sales/complete", oversell);
        Assert.Equal(HttpStatusCode.Conflict, oversellResponse.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CommerceDbContext>();
        Assert.Equal(0, await db.Sales.CountAsync());
        Assert.False(await db.InventoryTransactions.AnyAsync(t => t.SourceDocumentType == "Sale"));
    }

    private static async Task<string> LoginAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest("admin@example.test", "ChangeMe123!"));
        response.EnsureSuccessStatusCode();
        var login = await response.Content.ReadFromJsonAsync<TestLoginResponse>(JsonOptions);
        return login!.AccessToken;
    }

    private sealed record TestLoginResponse(string AccessToken);
    private sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);
}

public sealed class TestAppFactory : WebApplicationFactory<Program>
{
    private readonly InMemoryDatabaseRoot _databaseRoot = new();
    private readonly string _databaseName = $"commerce-{Guid.NewGuid()}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration(configuration =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AUTH_SIGNING_KEY"] = "testing-signing-key"
            });
        });
        builder.ConfigureServices(services =>
        {
            var descriptors = services
                .Where(d =>
                    d.ServiceType == typeof(DbContextOptions) ||
                    d.ServiceType == typeof(DbContextOptions<CommerceDbContext>) ||
                    d.ServiceType.FullName?.Contains("IDbContextOptionsConfiguration", StringComparison.Ordinal) == true)
                .ToArray();
            foreach (var descriptor in descriptors)
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<CommerceDbContext>(options => options
                .UseInMemoryDatabase(_databaseName, _databaseRoot)
                .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
            using var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();
            var seeder = scope.ServiceProvider.GetRequiredService<DevelopmentSeeder>();
            seeder.SeedAsync(CancellationToken.None).GetAwaiter().GetResult();
        });
    }
}
