using System.Data;
using Logaffe.Application.Ports;
using Logaffe.Domain.Entries;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

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
/// The write is the exception to that and the reason the folder exists: entries
/// arrive as batches and are never updated, so change tracking is pure overhead
/// for a row that will never change and row-by-row <c>INSERT</c> loses to
/// <c>COPY</c> by a wide margin. It goes to Npgsql directly, on the connection
/// EF Core is holding.
/// </para>
/// <para>
/// The others are sent through EF Core's raw channel rather than through Dapper,
/// which is what ADR 0003 says the log path reads with. Nothing here reads: a
/// delete answers with a row count and the one query answers with identities,
/// so there is no mapping to do. The reads that do have rows to turn back into
/// entries are <see cref="EntryReader"/>'s, and that is where Dapper is.
/// </para>
/// </remarks>
public sealed class Entries(LogaffeDbContext context) : IEntries
{
    /// <summary>
    /// The columns, in the order the rows below write them.
    /// </summary>
    /// <remarks>
    /// Written out rather than left to the table's own order, because binary
    /// <c>COPY</c> matches values to columns by position and nothing checks the
    /// names: a column added to the migration in the middle of this list would
    /// otherwise silently shift every value after it into the wrong column.
    /// </remarks>
    private const string Copy =
        """
        copy log_entry (
            id, project_id, event_time, receipt_time, level,
            logger_name, instance, trace_id, span_id,
            message_template, rendered_message, exception, properties,
            message_truncated, exception_truncated)
        from stdin (format binary)
        """;

    public async Task WriteAsync(
        IReadOnlyList<LogEntry> batch, CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)context.Database.GetDbConnection();

        // EF Core opens and closes around each of its own calls, and this is not
        // one of them. Whatever the connection's state was on the way in is what
        // it is on the way out, so a batch written inside somebody else's open
        // connection does not close it underneath them.
        var wasClosed = connection.State is not ConnectionState.Open;
        if (wasClosed)
        {
            await context.Database.OpenConnectionAsync(cancellationToken);
        }

        try
        {
            await using var writer =
                await connection.BeginBinaryImportAsync(Copy, cancellationToken);

            foreach (var entry in batch)
            {
                await writer.StartRowAsync(cancellationToken);

                await writer.WriteAsync(entry.Id, NpgsqlDbType.Bigint, cancellationToken);
                await writer.WriteAsync(entry.ProjectId, NpgsqlDbType.Uuid, cancellationToken);

                // As the instant each of them is. `timestamptz` holds no offset
                // of its own, so the sender's is read here and kept nowhere —
                // which is the schema's decision and not this one's.
                await writer.WriteAsync(
                    entry.EventTime.UtcDateTime, NpgsqlDbType.TimestampTz, cancellationToken);
                await writer.WriteAsync(
                    entry.ReceiptTime.UtcDateTime, NpgsqlDbType.TimestampTz, cancellationToken);

                // The number, because the partial index of docs/storage.md is
                // defined over `level >= 3`.
                await writer.WriteAsync(
                    (short)entry.Level, NpgsqlDbType.Smallint, cancellationToken);

                await WriteAsync(writer, entry.LoggerName, NpgsqlDbType.Text, cancellationToken);
                await WriteAsync(writer, entry.Instance, NpgsqlDbType.Text, cancellationToken);
                await WriteAsync(writer, entry.TraceId, NpgsqlDbType.Bytea, cancellationToken);
                await WriteAsync(writer, entry.SpanId, NpgsqlDbType.Bytea, cancellationToken);

                await writer.WriteAsync(
                    entry.MessageTemplate, NpgsqlDbType.Text, cancellationToken);
                await writer.WriteAsync(
                    entry.RenderedMessage, NpgsqlDbType.Text, cancellationToken);
                await WriteAsync(writer, entry.Exception, NpgsqlDbType.Text, cancellationToken);
                await WriteAsync(writer, entry.Properties, NpgsqlDbType.Jsonb, cancellationToken);

                await writer.WriteAsync(
                    entry.MessageTruncated, NpgsqlDbType.Boolean, cancellationToken);
                await writer.WriteAsync(
                    entry.ExceptionTruncated, NpgsqlDbType.Boolean, cancellationToken);
            }

            // Nothing is in the table until this returns, and a writer disposed
            // without it rolls the whole batch back — which is the all-or-nothing
            // the port promises.
            await writer.CompleteAsync(cancellationToken);
        }
        finally
        {
            if (wasClosed)
            {
                await context.Database.CloseConnectionAsync();
            }
        }
    }

    /// <summary>
    /// A column a delivery need not have filled. Promotion asks nothing of a
    /// sender, so four of the fifteen are absent in the ordinary case.
    /// </summary>
    private static async Task WriteAsync<TValue>(
        NpgsqlBinaryImporter writer,
        TValue? value,
        NpgsqlDbType type,
        CancellationToken cancellationToken)
        where TValue : class
    {
        if (value is null)
        {
            await writer.WriteNullAsync(cancellationToken);
        }
        else
        {
            await writer.WriteAsync(value, type, cancellationToken);
        }
    }

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
