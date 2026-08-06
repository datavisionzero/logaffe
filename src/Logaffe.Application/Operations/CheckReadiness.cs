using Logaffe.Application.Ports;

namespace Logaffe.Application.Operations;

/// <summary>
/// Whether the installation can serve.
/// </summary>
public enum Readiness
{
    /// <summary>The database cannot be reached.</summary>
    Unreachable,

    /// <summary>
    /// The database is reachable but the schema is not current. During a long
    /// migration on a large installation this is the honest answer, since
    /// nothing can be served yet.
    /// </summary>
    Migrating,

    /// <summary>The database is reachable and migrations are complete.</summary>
    Ready,
}

/// <summary>
/// The one question behind the health endpoint of <c>docs/operations.md</c>:
/// ready when the database is reachable <em>and</em> migrations are complete.
/// </summary>
/// <remarks>
/// The rule lives here rather than in the adapter that answers it, because the
/// endpoint is not the only thing that will ever want to ask, and because what
/// counts as ready is a decision about the product rather than about HTTP.
/// </remarks>
public sealed class CheckReadiness(IDatabaseProbe database)
{
    public async Task<Readiness> ExecuteAsync(CancellationToken cancellationToken)
    {
        if (!await database.CanConnectAsync(cancellationToken))
        {
            return Readiness.Unreachable;
        }

        return await database.HasPendingMigrationsAsync(cancellationToken)
            ? Readiness.Migrating
            : Readiness.Ready;
    }
}
