using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pos.Application.Common;
using Pos.Infrastructure.Persistence;

namespace Pos.Application.Customers;

public sealed class CustomerService
{
    private readonly CommerceDbContext _db;
    private readonly ITenantContext _tenant;

    public CustomerService(CommerceDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<IReadOnlyList<CustomerResponse>> ListAsync(string? q, CancellationToken cancellationToken)
    {
        var query = _db.Customers.AsNoTracking().Where(c => c.TenantId == _tenant.TenantId);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var needle = q.Trim().ToLowerInvariant();
            query = query.Where(c => c.DisplayName.ToLower().Contains(needle) || (c.Phone != null && c.Phone.Contains(needle)));
        }

        return await query.OrderBy(c => c.DisplayName).Select(c => ToResponse(c)).ToArrayAsync(cancellationToken);
    }

    public async Task<CustomerResponse> CreateAsync(CreateCustomerRequest request, CancellationToken cancellationToken)
    {
        Validate(request);
        var customer = new Customer
        {
            Id = Guid.NewGuid(),
            TenantId = _tenant.TenantId,
            DisplayName = request.DisplayName.Trim(),
            Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim(),
            Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim(),
            Gstin = string.IsNullOrWhiteSpace(request.Gstin) ? null : request.Gstin.Trim().ToUpperInvariant(),
            CreditLimit = request.CreditLimit,
            IsWalkIn = false
        };

        _db.Customers.Add(customer);
        _db.AuditLogs.Add(Audit("CustomerCreated", "Customer", customer.Id, customer));
        _db.OutboxEvents.Add(Outbox("CustomerCreated", customer));
        await _db.SaveChangesAsync(cancellationToken);
        return ToResponse(customer);
    }

    private static CustomerResponse ToResponse(Customer c) => new(c.Id, c.DisplayName, c.Phone, c.Email, c.Gstin, c.CreditLimit, c.IsWalkIn);

    private static void Validate(CreateCustomerRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.DisplayName)) errors["displayName"] = ["Display name is required."];
        if (request.CreditLimit < 0) errors["creditLimit"] = ["Credit limit cannot be negative."];
        if (!string.IsNullOrWhiteSpace(request.Email) && !request.Email.Contains('@')) errors["email"] = ["Email is invalid."];
        if (errors.Count > 0) throw new AppValidationException(errors);
    }

    private AuditLog Audit(string action, string entityName, Guid entityId, object after) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = _tenant.TenantId,
        ActorUserId = _tenant.UserId,
        Action = action,
        EntityName = entityName,
        EntityId = entityId,
        OccurredAtUtc = DateTimeOffset.UtcNow,
        AfterJson = JsonSerializer.Serialize(after)
    };

    private OutboxEvent Outbox(string eventType, object payload) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = _tenant.TenantId,
        EventType = eventType,
        PayloadJson = JsonSerializer.Serialize(payload),
        OccurredAtUtc = DateTimeOffset.UtcNow
    };
}
