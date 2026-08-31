using Pos.Domain.Common;

namespace Pos.Domain.Operations;

public sealed record AuditLog : TenantEntity
{
    public Guid? ActorUserId { get; init; }
    public required string Action { get; init; }
    public required string EntityName { get; init; }
    public required Guid EntityId { get; init; }
    public Guid? TerminalId { get; init; }
    public required DateTimeOffset OccurredAtUtc { get; init; }
    public string? BeforeJson { get; init; }
    public string? AfterJson { get; init; }
}

public sealed record OutboxEvent : TenantEntity
{
    public required string EventType { get; init; }
    public required string PayloadJson { get; init; }
    public required DateTimeOffset OccurredAtUtc { get; init; }
    public DateTimeOffset? ProcessedAtUtc { get; init; }
    public int Attempts { get; init; }
}
