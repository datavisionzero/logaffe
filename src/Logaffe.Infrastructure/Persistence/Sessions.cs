using Logaffe.Application.Ports;
using Logaffe.Domain.Identities;
using Microsoft.EntityFrameworkCore;

namespace Logaffe.Infrastructure.Persistence;

/// <summary>
/// The signed-in browsers of an installation.
/// </summary>
/// <remarks>
/// The table is read whole to authenticate, because a session secret names no
/// row (ADR 0031). That is affordable for exactly the reason it would not be for
/// tokens: an installation of this size holds a handful of rows, and they are
/// the browsers a handful of people are signed in on. Every other read here
/// narrows to one user, because a session list is a record of where a person has
/// been and nobody is shown anybody else's.
/// </remarks>
public sealed class Sessions(LogaffeDbContext context) : ISessions
{
    public async Task<IReadOnlyList<Session>> ListAsync(CancellationToken cancellationToken) =>
        await context.Sessions
            .OrderByDescending(s => s.StartedAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Session>> ListForAsync(
        Guid userId, CancellationToken cancellationToken) =>
        await context.Sessions
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.StartedAt)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(Session session, CancellationToken cancellationToken)
    {
        context.Sessions.Add(session);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveAsync(Session session, CancellationToken cancellationToken)
    {
        context.Sessions.Remove(session);
        await context.SaveChangesAsync(cancellationToken);
    }

    // One statement rather than a read and a delete per row: what is being asked
    // is "everything of mine but this", and there is nothing the caller wants
    // back. It never reaches anybody else's, which is what the second clause is
    // for.
    public Task RemoveEveryOtherAsync(Session kept, CancellationToken cancellationToken) =>
        context.Sessions
            .Where(s => s.UserId == kept.UserId && s.Id != kept.Id)
            .ExecuteDeleteAsync(cancellationToken);

    public Task RemoveEveryOfAsync(Guid userId, CancellationToken cancellationToken) =>
        context.Sessions.Where(s => s.UserId == userId).ExecuteDeleteAsync(cancellationToken);

    public Task RemoveExpiredAsync(DateTimeOffset asOf, CancellationToken cancellationToken)
    {
        // Both deadlines are derived from a date on the row, so the query is
        // the same arithmetic run backwards — which keeps it something the
        // database can answer without reading the rows.
        var untouchedSince = asOf - Session.IdleLifetime;
        var startedBefore = asOf - Session.AbsoluteLifetime;

        return context.Sessions
            .Where(s => s.LastUsedAt <= untouchedSince || s.StartedAt <= startedBefore)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task RecordUseAsync(Session session, CancellationToken cancellationToken) =>
        await context.SaveChangesAsync(cancellationToken);
}
