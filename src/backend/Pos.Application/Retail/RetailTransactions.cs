using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pos.Application.Common;
using Pos.Infrastructure.Persistence;

namespace Pos.Application.Retail;

internal static class RetailTransactions
{
    public static string Hash(object value) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));
    public static async Task<T> RunAsync<T>(CommerceDbContext db, Func<CancellationToken, Task<T>> work, CancellationToken ct)
    {
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            try
            {
                await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
                var result = await work(ct);
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return result;
            }
            catch (Exception ex) when (DatabaseConcurrency.IsSerializationFailure(ex))
            {
                if (attempt == 5) throw new ConflictException("Concurrent retail operation. Retry the original command.");
                db.ChangeTracker.Clear();
                await Task.Delay(Random.Shared.Next(100, 250) * (1 << (attempt - 1)), ct);
            }
        }
        throw new ConflictException("Concurrent retail operation. Retry the original command.");
    }

    public static void Audit(CommerceDbContext db, ITenantContext tenant, string action, string entity, Guid id, Guid terminalId, object value)
    {
        var json = JsonSerializer.Serialize(value);
        db.AuditLogs.Add(new AuditLog { Id = Guid.NewGuid(), TenantId = tenant.TenantId, ActorUserId = tenant.UserId,
            Action = action, EntityName = entity, EntityId = id, TerminalId = terminalId, OccurredAtUtc = DateTimeOffset.UtcNow, AfterJson = json });
        db.OutboxEvents.Add(new OutboxEvent { Id = Guid.NewGuid(), TenantId = tenant.TenantId, EventType = action, PayloadJson = json, OccurredAtUtc = DateTimeOffset.UtcNow });
    }
    public static string Reason(string? value) => !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= 300 ? value.Trim() : throw Invalid("reason", "A reason of at most 300 characters is required.");
    public static void Money(decimal amount, bool positive = false)
    {
        if (amount < 0 || (positive && amount == 0) || amount > 9_999_999_999_999_999.99m || amount != decimal.Round(amount, 2))
            throw Invalid("amount", "Use a valid non-negative two-decimal amount.");
    }
    public static AppValidationException Invalid(string field, string message) => new(new Dictionary<string, string[]> { [field] = [message] });
}
