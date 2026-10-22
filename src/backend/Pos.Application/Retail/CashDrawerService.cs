using Microsoft.EntityFrameworkCore;
using Pos.Application.Common;
using Pos.Domain.Tenancy;
using Pos.Infrastructure.Persistence;

namespace Pos.Application.Retail;

public sealed class CashDrawerService(CommerceDbContext db, ITenantContext tenant)
{
    public async Task<IReadOnlyList<DrawerSessionResponse>> ListAsync(Guid terminalId, CancellationToken ct)
    {
        if (!await db.Terminals.AnyAsync(t => t.TenantId == tenant.TenantId && t.Id == terminalId, ct)) throw new NotFoundException("Terminal not found.");
        var ids = await db.CashDrawerSessions.AsNoTracking().Where(s => s.TenantId == tenant.TenantId && s.TerminalId == terminalId)
            .OrderByDescending(s => s.OpenedAtUtc).Take(20).Select(s => s.Id).ToArrayAsync(ct);
        var result = new List<DrawerSessionResponse>();
        foreach (var id in ids) result.Add(await LoadAsync(id, ct));
        return result;
    }

    public async Task<DrawerSessionResponse> OpenAsync(Guid terminalId, OpenDrawerRequest request, CancellationToken ct)
    {
        RetailTransactions.Money(request.OpeningFloat);
        Command(request.ClientGeneratedId);
        var hash = RetailTransactions.Hash(new { terminalId, request.OpeningFloat });
        var id = await RetailTransactions.RunAsync(db, async token =>
        {
            await LockAsync(terminalId, token);
            var replay = await db.CashDrawerSessions.SingleOrDefaultAsync(s => s.TenantId == tenant.TenantId && s.TerminalId == terminalId && s.OpenCommandId == request.ClientGeneratedId, token);
            if (replay is not null) { Match(hash, replay.OpenRequestHash); return replay.Id; }
            if (!await db.Terminals.AnyAsync(t => t.TenantId == tenant.TenantId && t.Id == terminalId && t.Status == TerminalStatus.Active, token)) throw new NotFoundException("Active terminal not found.");
            if (await db.CashDrawerSessions.AnyAsync(s => s.TenantId == tenant.TenantId && s.TerminalId == terminalId && s.ClosedAtUtc == null, token)) throw new ConflictException("This terminal already has an open drawer.");
            var session = new CashDrawerSession { Id = Guid.NewGuid(), TenantId = tenant.TenantId, TerminalId = terminalId, OpenCommandId = request.ClientGeneratedId,
                OpenRequestHash = hash, OpeningFloat = request.OpeningFloat, OpenedByUserId = tenant.UserId, OpenedAtUtc = DateTimeOffset.UtcNow };
            db.CashDrawerSessions.Add(session);
            RetailTransactions.Audit(db, tenant, "DrawerOpened", "CashDrawerSession", session.Id, terminalId, new { session.Id, session.OpeningFloat });
            return session.Id;
        }, ct);
        return await LoadAsync(id, ct);
    }

    public async Task<DrawerSessionResponse> CloseAsync(Guid id, CloseDrawerRequest request, CancellationToken ct)
    {
        RetailTransactions.Money(request.CountedCash);
        Command(request.ClientGeneratedId);
        if (request.Note?.Length > 300) throw RetailTransactions.Invalid("note", "Note must be at most 300 characters.");
        var hash = RetailTransactions.Hash(new { request.CountedCash, Note = request.Note?.Trim() });
        await RetailTransactions.RunAsync(db, async token =>
        {
            var terminal = await db.CashDrawerSessions.Where(s => s.TenantId == tenant.TenantId && s.Id == id).Select(s => (Guid?)s.TerminalId).SingleOrDefaultAsync(token) ?? throw new NotFoundException("Drawer session not found.");
            await LockAsync(terminal, token);
            var session = await db.CashDrawerSessions.SingleAsync(s => s.TenantId == tenant.TenantId && s.Id == id, token);
            if (session.ClosedAtUtc is not null)
            {
                if (session.CloseCommandId != request.ClientGeneratedId) throw new ConflictException("Drawer is already closed.");
                Match(hash, session.CloseRequestHash!); return id;
            }
            session.ExpectedAtClose = await ExpectedAsync(session, token);
            session.CountedAtClose = request.CountedCash;
            session.Variance = request.CountedCash - session.ExpectedAtClose;
            session.CloseCommandId = request.ClientGeneratedId; session.CloseRequestHash = hash;
            session.ClosedAtUtc = DateTimeOffset.UtcNow; session.ClosedByUserId = tenant.UserId; session.CloseNote = request.Note?.Trim();
            RetailTransactions.Audit(db, tenant, "DrawerClosed", "CashDrawerSession", id, terminal, new { id, session.ExpectedAtClose, session.CountedAtClose, session.Variance, session.CloseNote });
            return id;
        }, ct);
        return await LoadAsync(id, ct);
    }

    public async Task<DrawerSessionResponse> MoveAsync(Guid id, DrawerMovementRequest request, CancellationToken ct)
    {
        Command(request.ClientGeneratedId); RetailTransactions.Money(request.Amount, true);
        var reason = RetailTransactions.Reason(request.Reason);
        if (request.Kind is not ("PaidIn" or "PaidOut")) throw RetailTransactions.Invalid("kind", "Choose PaidIn or PaidOut.");
        var hash = RetailTransactions.Hash(new { request.Kind, request.Amount, reason });
        await RetailTransactions.RunAsync(db, async token =>
        {
            var terminal = await db.CashDrawerSessions.Where(s => s.TenantId == tenant.TenantId && s.Id == id).Select(s => (Guid?)s.TerminalId).SingleOrDefaultAsync(token) ?? throw new NotFoundException("Drawer session not found.");
            await LockAsync(terminal, token);
            var replay = await db.CashDrawerMovements.SingleOrDefaultAsync(m => m.TenantId == tenant.TenantId && m.SessionId == id && m.SourceId == request.ClientGeneratedId, token);
            if (replay is not null) { Match(hash, replay.RequestHash ?? ""); return id; }
            var session = await db.CashDrawerSessions.SingleAsync(s => s.TenantId == tenant.TenantId && s.Id == id, token);
            if (session.ClosedAtUtc is not null) throw new ConflictException("Drawer is closed.");
            var delta = request.Kind == "PaidIn" ? request.Amount : -request.Amount;
            await AppendAsync(session, request.Kind, request.ClientGeneratedId, delta, reason, hash, token);
            return id;
        }, ct);
        return await LoadAsync(id, ct);
    }

    // Called inside the sale/correction transaction; never commit independently here.
    public async Task CaptureAsync(Guid terminalId, Guid sourceId, decimal delta, bool refund, CancellationToken ct)
    {
        if (delta == 0) return;
        await LockAsync(terminalId, ct);
        var session = await db.CashDrawerSessions.SingleOrDefaultAsync(s => s.TenantId == tenant.TenantId && s.TerminalId == terminalId && s.ClosedAtUtc == null, ct);
        if (session is null)
        {
            if (refund) throw new ConflictException("Open a drawer at the processing terminal before a cash refund.");
            return;
        }
        await AppendAsync(session, refund ? "Refund" : "Sale", sourceId, delta, refund ? "Sale correction" : "Cash sale allocation", null, ct);
    }

    private async Task AppendAsync(CashDrawerSession session, string kind, Guid sourceId, decimal delta, string reason, string? hash, CancellationToken ct)
    {
        if (delta < 0 && await ExpectedAsync(session, ct) + delta < 0) throw new ConflictException("Insufficient expected cash in this drawer.");
        var movement = new CashDrawerMovement { Id = Guid.NewGuid(), TenantId = tenant.TenantId, SessionId = session.Id, Kind = kind, SourceId = sourceId,
            RequestHash = hash, AmountDelta = delta, Reason = reason, ActorUserId = tenant.UserId, OccurredAtUtc = DateTimeOffset.UtcNow };
        db.CashDrawerMovements.Add(movement);
        // Also write the session so a snapshot older than close/capture cannot silently append to it.
        db.Entry(session).Property(s => s.OpeningFloat).IsModified = true;
        RetailTransactions.Audit(db, tenant, "DrawerMovementRecorded", "CashDrawerMovement", movement.Id, session.TerminalId,
            new { movement.SessionId, movement.Kind, movement.SourceId, movement.AmountDelta, movement.Reason });
    }
    private async Task<decimal> ExpectedAsync(CashDrawerSession session, CancellationToken ct) => session.OpeningFloat +
        (await db.CashDrawerMovements.Where(m => m.TenantId == tenant.TenantId && m.SessionId == session.Id).SumAsync(m => (decimal?)m.AmountDelta, ct) ?? 0);
    private async Task<DrawerSessionResponse> LoadAsync(Guid id, CancellationToken ct)
    {
        var s = await db.CashDrawerSessions.AsNoTracking().SingleOrDefaultAsync(s => s.TenantId == tenant.TenantId && s.Id == id, ct) ?? throw new NotFoundException("Drawer session not found.");
        var movements = await db.CashDrawerMovements.AsNoTracking().Where(m => m.TenantId == tenant.TenantId && m.SessionId == id).OrderBy(m => m.OccurredAtUtc).ThenBy(m => m.Id)
            .Select(m => new DrawerMovementResponse(m.Id, m.Kind, m.SourceId, m.AmountDelta, m.Reason, m.ActorUserId, m.OccurredAtUtc)).ToArrayAsync(ct);
        return new(s.Id, s.TerminalId, s.OpenedAtUtc, s.ClosedAtUtc, s.OpeningFloat, s.ExpectedAtClose ?? s.OpeningFloat + movements.Sum(m => m.AmountDelta), s.CountedAtClose, s.Variance, s.CloseNote, movements);
    }
    private Task LockAsync(Guid terminal, CancellationToken ct) => TransactionLocks.LockAsync(db, $"drawer:{tenant.TenantId}:{terminal}", ct);
    private static void Command(Guid id) { if (id == Guid.Empty) throw RetailTransactions.Invalid("clientGeneratedId", "A permanent command UUID is required."); }
    private static void Match(string expected, string actual) { if (expected != actual) throw new ConflictException("Command UUID was already used with a different payload."); }
}
