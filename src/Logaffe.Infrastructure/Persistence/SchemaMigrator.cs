using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Logaffe.Infrastructure.Persistence;

/// <summary>
/// Applies pending migrations on startup, which is what lets an upgrade be
/// <c>docker compose pull</c> and <c>docker compose up</c> with no step for the
/// operator to run.
/// </summary>
/// <remarks>
/// <c>docs/operations.md</c> asks for three things and this is the first of
/// them: <strong>migrations take a lock</strong>, so that two containers
/// starting at once do not migrate against each other — the second waits and
/// then finds nothing to do. A session-level advisory lock is the cheapest way
/// to say that in Postgres, because it needs no table that a migration might
/// itself be creating.
/// </remarks>
public sealed class SchemaMigrator(LogaffeDbContext context, ILogger<SchemaMigrator> logger)
{
    /// <summary>
    /// Any constant serves, as long as every logaffe uses the same one.
    /// </summary>
    private const long AdvisoryLockKey = 0x10CAFFE;

    public async Task ApplyAsync(CancellationToken cancellationToken)
    {
        await context.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await context.Database.ExecuteSqlRawAsync(
                "SELECT pg_advisory_lock({0})", [AdvisoryLockKey], cancellationToken);

            var pending = (await context.Database.GetPendingMigrationsAsync(cancellationToken))
                .ToArray();

            if (pending.Length == 0)
            {
                logger.LogInformation("Schema is current; nothing to migrate.");
                return;
            }

            logger.LogInformation(
                "Applying {Count} migration(s): {Migrations}", pending.Length, pending);

            await context.Database.MigrateAsync(cancellationToken);

            logger.LogInformation("Schema is current.");
        }
        finally
        {
            await context.Database.ExecuteSqlRawAsync(
                "SELECT pg_advisory_unlock({0})", [AdvisoryLockKey], CancellationToken.None);
            await context.Database.CloseConnectionAsync();
        }
    }
}
