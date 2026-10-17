using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Pos.Edge;
using Pos.Sync.Contracts;

var store = new EdgeStore(Environment.GetEnvironmentVariable("PILOT_EDGE_DB") ?? Path.Combine(".local", "edge.db"));
try
{
    switch (args.FirstOrDefault())
    {
        case "status":
            Console.WriteLine(JsonSerializer.Serialize(store.Status(), EdgeStore.JsonOptions));
            break;
        case "enqueue" when args.Length == 2:
            store.Enqueue(await File.ReadAllTextAsync(args[1]));
            Console.WriteLine("Sale request queued.");
            break;
        case "sync":
            var address = new Uri(Required("PILOT_SERVER_URL"));
            if (address.Scheme != "https" && !(address.Scheme == "http" && address.IsLoopback))
                throw new InvalidOperationException("Use HTTPS, except for a loopback development API.");
            using (var http = new HttpClient { BaseAddress = address, Timeout = TimeSpan.FromSeconds(60) })
            {
                using var auth = await http.PostAsJsonAsync("/api/v1/auth/login", new { email = Required("PILOT_EDGE_EMAIL"), password = Required("PILOT_EDGE_PASSWORD") });
                auth.EnsureSuccessStatusCode();
                var identity = await auth.Content.ReadFromJsonAsync<JsonElement>();
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", identity.GetProperty("accessToken").GetString());
                var tenantId = identity.GetProperty("tenantId").GetGuid();
                if (store.Binding is { } binding && (binding.TenantId != tenantId || binding.Server != address.AbsoluteUri))
                    throw new InvalidOperationException("The login or server does not match this local database.");
                if (store.Binding is null)
                {
                    var terminal = Guid.Parse(Required("PILOT_TERMINAL_ID"));
                    using var registered = await http.PostAsJsonAsync("/api/v1/sync/devices", new RegisterDeviceRequest(terminal, store.DeviceId));
                    registered.EnsureSuccessStatusCode();
                    var snapshot = await http.GetFromJsonAsync<SyncSnapshot>($"/api/v1/sync/devices/{store.DeviceId}/snapshot", EdgeStore.JsonOptions)
                        ?? throw new InvalidOperationException("Missing bootstrap snapshot.");
                    if (snapshot.TenantId != tenantId) throw new InvalidOperationException("Tenant mismatch.");
                    store.Initialize(snapshot, address.AbsoluteUri);
                }
                await new EdgeSynchronizer(http, store).SyncAsync(CancellationToken.None);
            }
            Console.WriteLine(JsonSerializer.Serialize(store.Status(), EdgeStore.JsonOptions));
            break;
        default:
            Console.Error.WriteLine("Usage: Pos.Edge status | sync | enqueue <sale-request.json>");
            return 2;
    }
    return 0;
}
catch (Exception ex) when (ex is not OutOfMemoryException)
{
    Console.Error.WriteLine($"Edge operation failed ({ex.GetType().Name}). State and queued requests are retained.");
    return 1;
}

static string Required(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
    ? value : throw new InvalidOperationException($"Set {name}.");
