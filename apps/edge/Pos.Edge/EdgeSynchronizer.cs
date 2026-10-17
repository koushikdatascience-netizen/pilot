using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Pos.Sync.Contracts;

namespace Pos.Edge;

public sealed class EdgeSynchronizer(HttpClient http, EdgeStore store, TimeProvider? clock = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    public async Task SyncAsync(CancellationToken ct)
    {
        var device = store.Binding ?? throw new InvalidOperationException("Bootstrap first.");
        for (var i = 0; i < 50; i++)
        {
            var sale = store.Claim(_clock.GetUtcNow());
            if (sale is null) break;
            try
            {
                using var response = await http.PostAsync($"/api/v1/sync/devices/{device.DeviceId}/sales", new StringContent(sale.Json, Encoding.UTF8, "application/json"), ct);
                if (response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync(ct);
                    using var json = JsonDocument.Parse(body);
                    if (json.RootElement.GetProperty("clientGeneratedId").GetGuid() != sale.Id ||
                        json.RootElement.GetProperty("status").GetString() != "Completed")
                        throw new InvalidOperationException("Server did not confirm this completed command.");
                    store.Finish(sale, "Synced", response: body);
                }
                else if ((int)response.StatusCode is >= 400 and < 500 && response.StatusCode is not
                    (System.Net.HttpStatusCode.TooManyRequests or System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.RequestTimeout))
                    store.Finish(sale, "Conflict", error: $"HTTP {(int)response.StatusCode}: operator review required");
                else
                {
                    Retry(sale);
                    break;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException or InvalidOperationException)
            {
                Retry(sale);
                throw;
            }
        }
        for (var i = 0; i < 100; i++)
        {
            var page = await http.GetFromJsonAsync<ChangePage>($"/api/v1/sync/devices/{device.DeviceId}/changes?after={store.Cursor}&limit=100", EdgeStore.JsonOptions, ct)
                ?? throw new InvalidOperationException("Missing change page.");
            store.Apply(page);
            using var acknowledgement = await http.PostAsJsonAsync($"/api/v1/sync/devices/{device.DeviceId}/ack", new AcknowledgeRequest(store.Cursor), ct);
            acknowledgement.EnsureSuccessStatusCode();
            if (!page.HasMore) break;
        }
    }

    private void Retry(QueuedSale sale) => store.Finish(sale, "Pending", error: "Transient failure; original command retained",
        retryAt: _clock.GetUtcNow().AddSeconds(Math.Min(300, Math.Pow(2, Math.Min(sale.Attempts, 8)))));
}
