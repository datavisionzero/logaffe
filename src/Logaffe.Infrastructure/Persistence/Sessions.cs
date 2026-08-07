using Logaffe.Application.Ports;
using Logaffe.Domain.Operators;
using Microsoft.EntityFrameworkCore;

namespace Logaffe.Infrastructure.Persistence;

/// <summary>
/// The operator's signed-in browsers.
/// </summary>
/// <remarks>
/// The table is read whole, because authenticating one compares against all of
/// them and the operator's list is all of them as well. That is affordable for
/// exactly the reason it would not be for tokens: there is one account, and the
/// rows are the browsers one person is signed in on.
/// </remarks>
public sealed class Sessions(LogaffeDbContext context) : ISessions
{
    public async Task<IReadOnlyList<Session>> ListAsync(CancellationToken cancellationToken) =>
        await context.Sessions
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
    // is "everything but this", and there is nothing the caller wants back.
    public Task RemoveEveryOtherAsync(Session kept, CancellationToken cancellationToken) =>
        context.Sessions.Where(s => s.Id != kept.Id).ExecuteDeleteAsync(cancellationToken);

    public Task RemoveExpiredAsync(DateTimeOffset asOf, CancellationToken cancellationToken)
    {
        // The deadline is derived from the last use, so the query is the same
        // arithmetic run backwards — which keeps it something the database can
        // answer without reading the rows.
        var untouchedSince = asOf - Session.SlidingLifetime;

        return context.Sessions
            .Where(s => s.LastUsedAt <= untouchedSince)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task RecordUseAsync(Session session, CancellationToken cancellationToken) =>
        await context.SaveChangesAsync(cancellationToken);
}
