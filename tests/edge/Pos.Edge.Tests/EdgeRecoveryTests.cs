using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Pos.Edge;
using Pos.Sync.Contracts;
using Xunit;

namespace Pos.Edge.Tests;

public sealed class EdgeRecoveryTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"pilot-edge-{Guid.NewGuid():N}");
    private string PathToDb => Path.Combine(_directory, "edge.db");
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _terminal = Guid.NewGuid();

    private EdgeStore Ready(params CatalogProduct[] products)
    {
        var store = new EdgeStore(PathToDb);
        store.Initialize(new SyncSnapshot(1, _tenant, _terminal, store.DeviceId, 0, products, []), "http://localhost/");
        return store;
    }

    [Fact]
    public void Device_binding_catalog_and_cursor_survive_restart()
    {
        var product = Product(1);
        var store = Ready(product);
        store.Apply(Page(1, product with { Version = 2, Name = "Updated" }));
        var reopened = new EdgeStore(PathToDb);
        Assert.Equal(store.DeviceId, reopened.DeviceId);
        Assert.Equal(1, reopened.Cursor);
        Assert.Equal("Updated", reopened.Product(product.Id)!.Name);
        Assert.Throws<InvalidOperationException>(() => reopened.Initialize(
            new SyncSnapshot(1, Guid.NewGuid(), _terminal, reopened.DeviceId, 0, [], []), "http://localhost/"));
    }

    [Fact]
    public void Malformed_page_rolls_back_cache_and_checkpoint_together()
    {
        var product = Product(1);
        var store = Ready(product);
        var first = Page(1, product with { Version = 2, Name = "Should roll back" }).Changes.Single();
        var bad = first with { Id = Guid.NewGuid(), EventId = Guid.NewGuid(), Sequence = 2, SchemaVersion = 2 };
        Assert.Throws<InvalidOperationException>(() => store.Apply(new ChangePage(1, 2, false, [first, bad])));
        Assert.Equal(0, store.Cursor);
        Assert.Equal(product.Name, store.Product(product.Id)!.Name);
    }

    [Fact]
    public void Delayed_events_cannot_overwrite_newer_bootstrap_versions()
    {
        var product = Product(5);
        var store = Ready(product);
        store.Apply(Page(1, product with { Version = 4, Name = "Stale" }));
        Assert.Equal(product.Name, store.Product(product.Id)!.Name);
        Assert.Equal(1, store.Cursor);
    }

    [Fact]
    public void Queue_is_immutable_and_crashed_leases_recover_without_new_ids()
    {
        var store = Ready();
        var id = Guid.NewGuid();
        var json = Sale(id);
        store.Enqueue(json);
        store.Enqueue(json);
        Assert.Throws<InvalidOperationException>(() => store.Enqueue(Sale(id, 2)));
        var now = DateTimeOffset.UtcNow.AddSeconds(1);
        var first = store.Claim(now)!;
        Assert.Null(store.Claim(now));
        var reopened = new EdgeStore(PathToDb);
        var recovered = reopened.Claim(now.AddMinutes(3))!;
        Assert.Equal(first.Id, recovered.Id);
        Assert.Equal(first.Json, recovered.Json);
        Assert.Equal(2, recovered.Attempts);
        store.Finish(first, "Synced");
        Assert.Equal(1, reopened.Status().InFlight);
        reopened.Finish(recovered, "Synced");
        Assert.Equal(1, reopened.Status().Synced);
    }

    [Fact]
    public void Queue_rejects_credential_fields_instead_of_persisting_them()
    {
        var store = Ready();
        var node = System.Text.Json.Nodes.JsonNode.Parse(Sale(Guid.NewGuid()))!;
        node["password"] = "must-not-persist";
        Assert.Throws<InvalidOperationException>(() => store.Enqueue(node.ToJsonString()));
        Assert.Equal(0, store.Status().Pending);
    }

    [Fact]
    public async Task Lost_upload_response_replays_original_id_and_acks_only_committed_pages()
    {
        var product = Product(1);
        var store = Ready(product);
        var id = Guid.NewGuid();
        store.Enqueue(Sale(id));
        var clock = new TestClock();
        var requests = new List<Guid>();
        var acknowledged = false;
        using var http = new HttpClient(new Handler(async request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/sales", StringComparison.Ordinal))
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
                var commandId = body.RootElement.GetProperty("clientGeneratedId").GetGuid();
                requests.Add(commandId);
                if (requests.Count == 1) throw new HttpRequestException("Connection lost after acceptance");
                return Json(new { clientGeneratedId = commandId, status = "Completed", id = Guid.NewGuid() });
            }
            if (request.RequestUri.AbsolutePath.EndsWith("/changes", StringComparison.Ordinal))
                return Json(Page(1, product with { Version = 2, Name = "Synced" }));
            Assert.Equal(1, new EdgeStore(PathToDb).Cursor);
            acknowledged = true;
            return Json(new { });
        })) { BaseAddress = new Uri("http://localhost/") };
        var sync = new EdgeSynchronizer(http, store, clock);
        await Assert.ThrowsAsync<HttpRequestException>(() => sync.SyncAsync(CancellationToken.None));
        Assert.Equal(1, store.Status().Pending);
        clock.Now = clock.Now.AddMinutes(1);
        await sync.SyncAsync(CancellationToken.None);
        Assert.Equal(new[] { id, id }, requests);
        Assert.True(acknowledged);
        Assert.Equal(1, store.Status().Synced);
        Assert.Equal("Synced", store.Product(product.Id)!.Name);
    }

    [Fact]
    public async Task Stock_conflict_is_retained_for_review_without_automatic_resubmission()
    {
        var store = Ready();
        store.Enqueue(Sale(Guid.NewGuid()));
        var uploads = 0;
        using var http = new HttpClient(new Handler(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/sales", StringComparison.Ordinal))
            {
                uploads++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Conflict));
            }
            return Task.FromResult(request.Method == HttpMethod.Get ? Json(new ChangePage(1, 0, false, [])) : Json(new { }));
        })) { BaseAddress = new Uri("http://localhost/") };
        var sync = new EdgeSynchronizer(http, store, new TestClock());
        await sync.SyncAsync(CancellationToken.None);
        await sync.SyncAsync(CancellationToken.None);
        Assert.Equal(1, uploads);
        Assert.Equal(1, store.Status().Conflicts);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(408)]
    [InlineData(429)]
    [InlineData(503)]
    public async Task Transient_http_failures_retain_pending_command_with_backoff(int status)
    {
        var store = Ready();
        store.Enqueue(Sale(Guid.NewGuid()));
        var uploads = 0;
        using var http = new HttpClient(new Handler(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/sales", StringComparison.Ordinal))
            {
                uploads++;
                return Task.FromResult(new HttpResponseMessage((HttpStatusCode)status));
            }
            return Task.FromResult(request.Method == HttpMethod.Get ? Json(new ChangePage(1, 0, false, [])) : Json(new { }));
        })) { BaseAddress = new Uri("http://localhost/") };
        var sync = new EdgeSynchronizer(http, store, new TestClock());
        await sync.SyncAsync(CancellationToken.None);
        await sync.SyncAsync(CancellationToken.None);
        Assert.Equal(1, uploads);
        Assert.Equal(1, store.Status().Pending);
        Assert.Equal(0, store.Status().Conflicts);
    }

    [Fact]
    public async Task Lost_acknowledgement_resumes_from_durable_checkpoint_after_restart()
    {
        var product = Product(1);
        var store = Ready(product);
        var acks = 0;
        using var http = new HttpClient(new Handler(request =>
        {
            if (request.Method == HttpMethod.Get)
                return Task.FromResult(Json(request.RequestUri!.Query.Contains("after=1", StringComparison.Ordinal)
                    ? new ChangePage(1, 1, false, []) : Page(1, product with { Version = 2 })));
            if (++acks == 1) throw new HttpRequestException("Acknowledgement response lost");
            return Task.FromResult(Json(new { }));
        })) { BaseAddress = new Uri("http://localhost/") };
        await Assert.ThrowsAsync<HttpRequestException>(() => new EdgeSynchronizer(http, store).SyncAsync(CancellationToken.None));
        var reopened = new EdgeStore(PathToDb);
        Assert.Equal(1, reopened.Cursor);
        await new EdgeSynchronizer(http, reopened).SyncAsync(CancellationToken.None);
        Assert.Equal(2, acks);
        Assert.Equal(2, reopened.Product(product.Id)!.Version);
    }

    [Fact]
    public void Customer_event_payload_is_not_cached_in_local_inbox()
    {
        var store = Ready();
        store.Apply(new ChangePage(1, 1, false,
            [new SyncChange(Guid.NewGuid(), 1, Guid.NewGuid(), 1, "CustomerUpdated",
                JsonSerializer.SerializeToElement(new { email = "private@example.test" }))]));
        using var db = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={PathToDb};Pooling=False");
        db.Open();
        using var command = db.CreateCommand();
        command.CommandText = "SELECT json FROM inbox";
        Assert.DoesNotContain("private@example.test", (string)command.ExecuteScalar()!);
        Assert.Equal(1, store.Cursor);
    }

    private string Sale(Guid id, int quantity = 1) => JsonSerializer.Serialize(new
    {
        clientGeneratedId = id, terminalId = _terminal, locationId = Guid.NewGuid(),
        lines = new[] { new { productId = Guid.NewGuid(), quantity, discountAmount = 0 } },
        payments = new[] { new { method = "Cash", amount = 10 } }
    });
    private static CatalogProduct Product(long version) => new(Guid.NewGuid(), "Milk", "MILK", "890", Guid.NewGuid(), Guid.NewGuid(), "0401", "pc", 68, "INR", true, true, version);
    private static ChangePage Page(long sequence, CatalogProduct product) => new(1, sequence, false,
        [new SyncChange(Guid.NewGuid(), sequence, Guid.NewGuid(), 1, "ProductUpdated", JsonSerializer.SerializeToElement(product))]);
    private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request); }
    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow.AddSeconds(1);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
