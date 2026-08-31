namespace Pos.Domain.Common;

public abstract record TenantEntity
{
    public required Guid Id { get; init; }
    public required Guid TenantId { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public long Version { get; init; } = 1;
}
