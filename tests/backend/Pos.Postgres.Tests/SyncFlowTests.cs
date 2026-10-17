using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pos.Application.Auth;
using Pos.Application.Catalog;
using Pos.Application.Sync;
using Pos.Application.Tenancy;
using Pos.Domain.Identity;
using Pos.Infrastructure.Persistence;
using Pos.Sync.Contracts;
using Xunit;

namespace Pos.Postgres.Tests;

[Collection(PostgresCollection.Name)]
public sealed class SyncFlowTests
{
    [Fact]
    public async Task Registration_is_idempotent_and_terminal_cannot_be_rebound_silently()
    {
        await using var factory = await PostgresTestAppFactory.CreateAsync();
        using var client = await Client(factory);
        var context = (await client.GetFromJsonAsync<BootstrapResponse>("/api/v1/tenancy/bootstrap"))!;
        var device = Guid.NewGuid();
        for (var i = 0; i < 2; i++)
            (await client.PostAsJsonAsync("/api/v1/sync/devices", new RegisterDeviceRequest(context.TerminalId, device))).EnsureSuccessStatusCode();
        var conflict = await client.PostAsJsonAsync("/api/v1/sync/devices", new RegisterDeviceRequest(context.TerminalId, Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        using var cashier = await Client(factory, "cashier@example.test");
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.PostAsJsonAsync("/api/v1/sync/devices", new RegisterDeviceRequest(context.TerminalId, Guid.NewGuid()))).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CommerceDbContext>();
        Assert.Equal(1, await db.TerminalDevices.CountAsync());
        Assert.Equal(1, await db.OutboxEvents.CountAsync(e => e.EventType == "TerminalRegistered"));
    }

    [Fact]
    public async Task Concurrent_publication_is_once_ordered_and_ack_is_bounded()
    {
        await using var factory = await PostgresTestAppFactory.CreateAsync();
        using var client = await Client(factory);
        var context = (await client.GetFromJsonAsync<BootstrapResponse>("/api/v1/tenancy/bootstrap"))!;
        var device = Guid.NewGuid();
        (await client.PostAsJsonAsync("/api/v1/sync/devices", new RegisterDeviceRequest(context.TerminalId, device))).EnsureSuccessStatusCode();
        await using (var seed = factory.Services.CreateAsyncScope())
        {
            var db = seed.ServiceProvider.GetRequiredService<CommerceDbContext>();
            for (var i = 0; i < 5; i++)
                db.OutboxEvents.Add(new OutboxEvent { Id = Guid.NewGuid(), TenantId = context.TenantId, EventType = "SyncTest", PayloadJson = "{}", OccurredAtUtc = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }
        async Task<int> Publish()
        {
            await using var scope = factory.Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<OutboxPublisher>().PublishAsync(CancellationToken.None);
        }
        var counts = await Task.WhenAll(Publish(), Publish());
        Assert.Equal(6, counts.Sum());
        Assert.Equal(0, await Publish());
        var page = (await client.GetFromJsonAsync<ChangePage>($"/api/v1/sync/devices/{device}/changes?limit=2"))!;
        Assert.Equal(2, page.Changes.Count);
        Assert.True(page.HasMore);
        Assert.True(page.Changes[0].Sequence < page.Changes[1].Sequence);
        var invalid = await client.PostAsJsonAsync($"/api/v1/sync/devices/{device}/ack", new AcknowledgeRequest(page.NextCursor + 100));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        (await client.PostAsJsonAsync($"/api/v1/sync/devices/{device}/ack", new AcknowledgeRequest(page.NextCursor))).EnsureSuccessStatusCode();
        var replay = await client.PostAsJsonAsync($"/api/v1/sync/devices/{device}/ack", new AcknowledgeRequest(0));
        Assert.Equal(page.NextCursor, (await replay.Content.ReadFromJsonAsync<DeviceResponse>())!.AcknowledgedSequence);
        await using var verify = factory.Services.CreateAsyncScope();
        var final = verify.ServiceProvider.GetRequiredService<CommerceDbContext>();
        Assert.Equal(6, await final.SyncChanges.CountAsync());
        Assert.False(await final.OutboxEvents.AnyAsync(e => e.ProcessedAtUtc == null));
    }

    [Fact]
    public async Task Snapshot_versions_and_later_product_events_match_saved_versions()
    {
        await using var factory = await PostgresTestAppFactory.CreateAsync();
        using var client = await Client(factory);
        var context = (await client.GetFromJsonAsync<BootstrapResponse>("/api/v1/tenancy/bootstrap"))!;
        var device = Guid.NewGuid();
        (await client.PostAsJsonAsync("/api/v1/sync/devices", new RegisterDeviceRequest(context.TerminalId, device))).EnsureSuccessStatusCode();
        var snapshotResponse = await client.GetAsync($"/api/v1/sync/devices/{device}/snapshot");
        Assert.True(snapshotResponse.IsSuccessStatusCode, await snapshotResponse.Content.ReadAsStringAsync());
        var snapshot = (await snapshotResponse.Content.ReadFromJsonAsync<SyncSnapshot>())!;
        var milk = snapshot.Products.Single(p => p.Sku == "MILK-1L");
        var update = await client.PutAsJsonAsync($"/api/v1/products/{milk.Id}", new UpdateProductRequest(milk.CategoryId, milk.TaxCategoryId, "New milk", milk.Sku, milk.Barcode, milk.HsnSac, milk.UnitOfMeasure, milk.PriceAmount, milk.Currency, true, true));
        update.EnsureSuccessStatusCode();
        await using (var publishing = factory.Services.CreateAsyncScope())
            await publishing.ServiceProvider.GetRequiredService<OutboxPublisher>().PublishAsync(CancellationToken.None);
        var page = (await client.GetFromJsonAsync<ChangePage>($"/api/v1/sync/devices/{device}/changes?after={snapshot.Cursor}"))!;
        var change = Assert.Single(page.Changes, c => c.EventType == "ProductUpdated");
        var payload = change.Payload.Deserialize<CatalogProduct>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        Assert.Equal(milk.Version + 1, payload.Version);
        await using var verify = factory.Services.CreateAsyncScope();
        Assert.Equal(payload.Version, (await verify.ServiceProvider.GetRequiredService<CommerceDbContext>().Products.SingleAsync(p => p.Id == milk.Id)).Version);
    }

    [Fact]
    public async Task Uncommitted_outbox_events_are_never_published_and_other_tenant_cannot_read_device()
    {
        await using var factory = await PostgresTestAppFactory.CreateAsync();
        using var client = await Client(factory);
        var boot = (await client.GetFromJsonAsync<BootstrapResponse>("/api/v1/tenancy/bootstrap"))!;
        var device = Guid.NewGuid();
        (await client.PostAsJsonAsync("/api/v1/sync/devices", new RegisterDeviceRequest(boot.TerminalId, device))).EnsureSuccessStatusCode();
        var abortedId = Guid.NewGuid();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CommerceDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync();
            db.OutboxEvents.Add(new OutboxEvent { Id = abortedId, TenantId = boot.TenantId, EventType = "Aborted", PayloadJson = "{}", OccurredAtUtc = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
            await transaction.RollbackAsync();
        }
        await using (var publish = factory.Services.CreateAsyncScope())
            await publish.ServiceProvider.GetRequiredService<OutboxPublisher>().PublishAsync(CancellationToken.None);
        await using (var verify = factory.Services.CreateAsyncScope())
        {
            var db = verify.ServiceProvider.GetRequiredService<CommerceDbContext>();
            Assert.False(await db.SyncChanges.AnyAsync(e => e.EventId == abortedId));
            var tenant = Guid.NewGuid();
            var user = Guid.NewGuid();
            var role = Guid.NewGuid();
            db.Tenants.Add(new Tenant { Id = tenant, Name = "Sync tenant B", Status = Pos.Domain.Tenancy.TenantStatus.Active });
            db.Users.Add(new Pos.Infrastructure.Persistence.User { Id = user, TenantId = tenant, DisplayName = "Other user", Email = "sync-b@example.test", PasswordHash = verify.ServiceProvider.GetRequiredService<Pos.Domain.Security.IPasswordHasher>().Hash("ChangeMe123!") });
            db.Roles.Add(new Pos.Infrastructure.Persistence.Role { Id = role, TenantId = tenant, Name = "Cashier" });
            db.UserRoles.Add(new UserRole { UserId = user, RoleId = role });
            db.RolePermissions.Add(new RolePermission { RoleId = role, Permission = Permission.CompleteSale });
            await db.SaveChangesAsync();
        }
        using var other = await Client(factory, "sync-b@example.test");
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/v1/sync/devices/{device}/changes")).StatusCode);
    }

    [Fact]
    public async Task Durable_edge_queue_uploads_through_registered_route_and_server_replays_once()
    {
        await using var factory = await PostgresTestAppFactory.CreateAsync();
        using var client = await Client(factory);
        var boot = (await client.GetFromJsonAsync<BootstrapResponse>("/api/v1/tenancy/bootstrap"))!;
        var directory = Path.Combine(Path.GetTempPath(), $"pilot-edge-server-{Guid.NewGuid():N}");
        try
        {
            var store = new Pos.Edge.EdgeStore(Path.Combine(directory, "edge.db"));
            (await client.PostAsJsonAsync("/api/v1/sync/devices", new RegisterDeviceRequest(boot.TerminalId, store.DeviceId))).EnsureSuccessStatusCode();
            var snapshot = (await client.GetFromJsonAsync<SyncSnapshot>($"/api/v1/sync/devices/{store.DeviceId}/snapshot"))!;
            store.Initialize(snapshot, "http://localhost/");
            var milk = snapshot.Products.Single(p => p.Sku == "MILK-1L");
            var commandId = Guid.NewGuid();
            var command = JsonSerializer.Serialize(new
            {
                locationId = boot.LocationId, terminalId = boot.TerminalId, clientGeneratedId = commandId,
                lines = new[] { new { productId = milk.Id, quantity = 2, discountAmount = 6 } },
                payments = new[] { new { method = "Cash", amount = 130, amountTendered = 150 } }
            });
            store.Enqueue(command);
            Assert.Equal(1, new Pos.Edge.EdgeStore(Path.Combine(directory, "edge.db")).Status().Pending);
            await new Pos.Edge.EdgeSynchronizer(client, store).SyncAsync(CancellationToken.None);
            Assert.Equal(1, store.Status().Synced);
            var replay = await client.PostAsync($"/api/v1/sync/devices/{store.DeviceId}/sales", new StringContent(command, System.Text.Encoding.UTF8, "application/json"));
            replay.EnsureSuccessStatusCode();
            Assert.True((await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("idempotentReplay").GetBoolean());
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<CommerceDbContext>();
            Assert.Equal(1, await db.Sales.CountAsync(s => s.ClientGeneratedId == commandId));
            var wrongTerminal = JsonSerializer.Serialize(new { locationId = boot.LocationId, terminalId = Guid.NewGuid(), clientGeneratedId = Guid.NewGuid(), lines = Array.Empty<object>(), payments = Array.Empty<object>() });
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync($"/api/v1/sync/devices/{store.DeviceId}/sales", new StringContent(wrongTerminal, System.Text.Encoding.UTF8, "application/json"))).StatusCode);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private static async Task<HttpClient> Client(PostgresTestAppFactory factory, string email = "admin@example.test")
    {
        var client = factory.CreateClient();
        var auth = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, "ChangeMe123!"));
        auth.EnsureSuccessStatusCode();
        var identity = await auth.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", identity.GetProperty("accessToken").GetString());
        return client;
    }
}
