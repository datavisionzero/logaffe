using Logaffe.Application.Ports;
using Logaffe.Domain.History;
using Microsoft.EntityFrameworkCore;

namespace Logaffe.Infrastructure.Persistence;

/// <inheritdoc cref="IHistory"/>
public sealed class Histories(LogaffeDbContext context) : IHistory
{
    public async Task RecordAsync(Change change, CancellationToken cancellationToken)
    {
        context.History.Add(change);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Change>> ListAsync(
        long? before, int limit, CancellationToken cancellationToken) =>
        await context.History
            .Where(change => before == null || change.Id < before)
            .OrderByDescending(change => change.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);
}
