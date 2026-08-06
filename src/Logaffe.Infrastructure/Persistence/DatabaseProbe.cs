using Logaffe.Application.Ports;
using Microsoft.EntityFrameworkCore;

namespace Logaffe.Infrastructure.Persistence;

/// <summary>
/// Answers the two questions of <see cref="IDatabaseProbe"/> and nothing more;
/// what counts as ready is decided a layer in.
/// </summary>
public sealed class DatabaseProbe(LogaffeDbContext context) : IDatabaseProbe
{
    public Task<bool> CanConnectAsync(CancellationToken cancellationToken) =>
        context.Database.CanConnectAsync(cancellationToken);

    public async Task<bool> HasPendingMigrationsAsync(CancellationToken cancellationToken)
    {
        var pending = await context.Database.GetPendingMigrationsAsync(cancellationToken);
        return pending.Any();
    }
}
