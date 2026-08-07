using Logaffe.Application.Ports;
using Microsoft.EntityFrameworkCore;

namespace Logaffe.Infrastructure.Persistence.Log;

/// <summary>
/// The log path's own SQL, kept in a folder of its own.
/// </summary>
/// <remarks>
/// <para>
/// ADR 0003 draws the boundary at this table rather than at a feature: EF Core
/// declares <c>log_entry</c> like every other table and serves none of it, so
/// nothing here materializes an entry, tracks one, or lets a LINQ provider
/// decide what the query looks like. The statements are written out because
/// they are fitted to the indexes <c>docs/storage.md</c> claims, and re-reading
/// them whenever an index changes is the standing cost that ADR names.
/// </para>
/// <para>
/// They are sent through EF Core's raw channel rather than through Dapper,
/// which is what ADR 0003 says the log path reads with. Nothing here reads: a
/// delete answers with a row count and the one query answers with identities,
/// so there is no mapping to do and no reason yet to take the dependency.
/// Dapper arrives with the filtered page that needs it.
/// </para>
/// </remarks>
public sealed class Entries(LogaffeDbContext context) : IEntries
{
    public async Task<IReadOnlyList<Guid>> ProjectsWithEntriesAsync(
        CancellationToken cancellationToken) =>
        // Distinct over the leading column of every index on the table, which
        // is what keeps this from being a walk over ten million rows to find
        // the twenty answers.
        await context.Database
            .SqlQuery<Guid>($"""select distinct project_id as "Value" from log_entry""")
            .ToListAsync(cancellationToken);

    public Task<int> RemoveReceivedBeforeAsync(
        Guid projectId,
        DateTimeOffset receivedBefore,
        int portion,
        CancellationToken cancellationToken) =>
        // The inner query is exactly the receipt index — (project_id,
        // receipt_time, id) — so choosing the portion reads only keys, and the
        // delete then goes by primary key. Bounded because the alternative
        // holds a long transaction across a table other projects are still
        // being written to (ADR 0023).
        context.Database.ExecuteSqlAsync(
            $"""
            delete from log_entry
            where id in (
                select id
                from log_entry
                where project_id = {projectId} and receipt_time < {receivedBefore}
                limit {portion})
            """,
            cancellationToken);

    public async Task<long> CountReceivedBeforeAsync(
        Guid projectId, DateTimeOffset receivedBefore, CancellationToken cancellationToken) =>
        // The receipt index again, and nothing but it: leading with the project
        // and ending at the cutoff makes this a walk over the keys that match
        // rather than a read of the rows behind them. It is one number, and it
        // is asked while an operator waits for the answer.
        await context.Database
            .SqlQuery<long>(
                $"""
                select count(*) as "Value"
                from log_entry
                where project_id = {projectId} and receipt_time < {receivedBefore}
                """)
            .SingleAsync(cancellationToken);
}
