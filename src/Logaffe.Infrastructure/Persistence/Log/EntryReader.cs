using System.Data;
using System.Globalization;
using Dapper;
using Logaffe.Application.Ports;
using Logaffe.Domain.Entries;
using Logaffe.Domain.Queries;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Logaffe.Infrastructure.Persistence.Log;

/// <summary>
/// The read side of the log path: the filtered page, the count, one entry, and
/// the live tail's poll.
/// </summary>
/// <remarks>
/// <para>
/// Hand-written and read with Dapper, which is what ADR 0003 said the log path
/// would need once something had rows to turn back into entries. The statements
/// are written out because they are fitted to the indexes
/// <c>docs/storage.md</c> claims — the paging index for the order and the
/// cursor, the receipt index for the tail, the trigram index for the search, the
/// partial one for the threshold people actually ask for — and re-reading them
/// whenever an index changes is the standing cost that ADR names.
/// </para>
/// <para>
/// <b>Every read here is held to five seconds</b> (ADR 0026). It is enforced by
/// the server rather than by a token this waits on, because a cancelled wait
/// leaves the statement running and the thing the limit exists to prevent is an
/// installation occupied by one request. A statement the server stops arrives as
/// <see cref="ReadExpiredException"/>; a caller that went away arrives as the
/// cancellation it is, and the two are told apart because only one of them is
/// this class's business.
/// </para>
/// </remarks>
public sealed class EntryReader(LogaffeDbContext context) : IEntryReader
{
    /// <summary>
    /// The columns an entry is read out of, aliased to the shape below.
    /// </summary>
    /// <remarks>
    /// Written out rather than <c>select *</c>: this is the widest table in the
    /// database and two of its columns are the kilobytes, so what a statement
    /// asks for is worth being able to see.
    /// </remarks>
    private const string Columns =
        """
        id as "Id", project_id as "ProjectId",
        event_time as "EventTime", receipt_time as "ReceiptTime", level as "LevelNumber",
        logger_name as "LoggerName", instance as "Instance",
        trace_id as "TraceId", span_id as "SpanId",
        message_template as "MessageTemplate", rendered_message as "RenderedMessage",
        exception as "Exception", properties as "Properties",
        message_truncated as "MessageTruncated", exception_truncated as "ExceptionTruncated"
        """;

    public async Task<IReadOnlyList<LogEntry>> PageAsync(
        Guid projectId,
        EntryFilters filters,
        EntryCursor? after,
        CancellationToken cancellationToken)
    {
        var (where, parameters) = EntryPredicate.For(projectId, filters);

        if (after is not null)
        {
            // The pair compared as a pair, which is what makes this a seek into
            // the paging index rather than a filter over what it returned:
            // (project_id, event_time desc, id desc) is the index, and this is a
            // position in it. Comparing the two halves separately with `or`
            // would be the same set of rows and a different plan.
            parameters.Add("cursorTime", after.EventTime.UtcDateTime);
            parameters.Add("cursorId", after.Id);
            where += " and (event_time, id) < (@cursorTime, @cursorId)";
        }

        parameters.Add("size", Page.Size);

        var sql =
            $"""
            select {Columns}
            from log_entry
            where {where}
            order by event_time desc, id desc
            limit @size
            """;

        var rows = await QueryAsync<Row>(sql, parameters, cancellationToken);

        return [.. rows.Select(row => row.Entry())];
    }

    public async Task<IReadOnlyList<LogEntry>> ArrivalsAsync(
        Guid projectId,
        EntryFilters filters,
        TailCursor since,
        CancellationToken cancellationToken)
    {
        var (where, parameters) = EntryPredicate.For(projectId, filters);

        // The pair compared as a pair again, and this time it is a position in
        // the receipt index — (project_id, receipt_time, id), which
        // docs/storage.md already carries for this and for the retention sweep.
        parameters.Add("sinceTime", since.ReceiptTime.UtcDateTime);
        parameters.Add("sinceId", since.Id);
        parameters.Add("size", Page.Size);

        // Two orders in one statement, and both are load-bearing. The inner one
        // is the arrival order, so what a poll that fills leaves behind is the
        // front of it and the cursor it hands back has nothing hiding under it.
        // The outer one is the view's, so the caller drops these rows into the
        // page it is holding without re-sorting it — and a late delivery lands
        // among the entries it belongs with rather than at the top (ADR 0009).
        var sql =
            $"""
            select * from (
                select {Columns}
                from log_entry
                where {where} and (receipt_time, id) > (@sinceTime, @sinceId)
                order by receipt_time, id
                limit @size
            ) arrived
            order by "EventTime" desc, "Id" desc
            """;

        var rows = await QueryAsync<Row>(sql, parameters, cancellationToken);

        return [.. rows.Select(row => row.Entry())];
    }

    public async Task<TailCursor?> NewestArrivalAsync(
        Guid projectId, CancellationToken cancellationToken)
    {
        // The end of the receipt index, which is one lookup and no filters: the
        // position a tail starts watching from is the same position whatever the
        // view is narrowed to.
        var sql =
            """
            select receipt_time as "ReceiptTime", id as "Id"
            from log_entry
            where project_id = @projectId
            order by receipt_time desc, id desc
            limit 1
            """;

        var parameters = new DynamicParameters();
        parameters.Add("projectId", projectId);

        var rows = await QueryAsync<Arrival>(sql, parameters, cancellationToken);

        return rows.Count == 0
            ? null
            : new TailCursor(new DateTimeOffset(rows[0].ReceiptTime, TimeSpan.Zero), rows[0].Id);
    }

    public async Task<DateTimeOffset?> LastReceivedAsync(
        Guid projectId, CancellationToken cancellationToken)
    {
        // The same end of the same receipt index the arming above reads, asked
        // for the fact rather than for a position: one column back, because
        // nothing on the row is shown. `max` over the identity would order by
        // the wrong half of the index.
        var sql =
            """
            select receipt_time
            from log_entry
            where project_id = @projectId
            order by receipt_time desc
            limit 1
            """;

        var parameters = new DynamicParameters();
        parameters.Add("projectId", projectId);

        var rows = await QueryAsync<DateTime?>(sql, parameters, cancellationToken);

        return rows.Count == 0 || rows[0] is not { } received
            ? null
            : new DateTimeOffset(received, TimeSpan.Zero);
    }

    public async Task<IReadOnlyList<CountedGroup>> CountAsync(
        Guid projectId,
        EntryFilters filters,
        Grouping grouping,
        TimeBucket bucket,
        CancellationToken cancellationToken)
    {
        var (where, parameters) = EntryPredicate.For(projectId, filters);

        // Ungrouped is one row and no `group by`, so that a caller reads every
        // count the same way and the plain number is the grouped answer with one
        // group in it.
        var sql = grouping is Grouping.None
            ? $"""
               select null::text as "Value", count(*) as "Entries"
               from log_entry
               where {where}
               """
            : $"""
               select {Grouped(grouping, bucket)} as "Value", count(*) as "Entries"
               from log_entry
               where {where}
               group by 1
               order by {Ordered(grouping)}
               """;

        var rows = await QueryAsync<CountedGroup>(sql, parameters, cancellationToken);

        return [.. rows];
    }

    public async Task<LogEntry?> FindAsync(
        Guid projectId, long id, CancellationToken cancellationToken)
    {
        // The project as well as the identity, and not because the identity is
        // not unique. It is asked so that an entry cannot be reached from a
        // project it does not belong to by guessing a number, which would be the
        // one hole in a separation the rest of the product keeps.
        var sql =
            $"""
            select {Columns}
            from log_entry
            where id = @id and project_id = @projectId
            """;

        var parameters = new DynamicParameters();
        parameters.Add("id", id);
        parameters.Add("projectId", projectId);

        var rows = await QueryAsync<Row>(sql, parameters, cancellationToken);

        return rows.SingleOrDefault()?.Entry();
    }

    /// <summary>
    /// The expression a count groups by, which is the column its filter already
    /// exists for.
    /// </summary>
    /// <remarks>
    /// Every one of these is a closed set of literals rather than anything a
    /// caller wrote: the grouping is an enum, and what reaches the statement is
    /// this method's own text.
    /// </remarks>
    private static string Grouped(Grouping grouping, TimeBucket bucket) => grouping switch
    {
        // As the number it is stored as, which the adapter turns back into a
        // name. Doing it here would mean the six names living in a SQL string
        // as well as in the enum that owns them.
        Grouping.Level => "level::text",
        Grouping.LoggerName => "logger_name",
        Grouping.Instance => "instance",

        // Aligned to the clock and not to the range asked for, so that the same
        // entry falls in the same bucket whatever window it is counted in — which
        // is what makes two counts of overlapping ranges comparable. Rendered as
        // an instant in UTC, because a bucket labelled in the server's zone is a
        // label nobody can read back.
        Grouping.Time =>
            $"""to_char(date_trunc('{Truncated(bucket)}', event_time at time zone 'UTC'), 'YYYY-MM-DD"T"HH24:MI:SS"Z"')""",

        _ => throw new ArgumentOutOfRangeException(nameof(grouping), grouping, null),
    };

    private static string Truncated(TimeBucket bucket) => bucket switch
    {
        TimeBucket.Minute => "minute",
        TimeBucket.Hour => "hour",
        TimeBucket.Day => "day",
        _ => throw new ArgumentOutOfRangeException(nameof(bucket), bucket, null),
    };

    /// <summary>
    /// What order the groups come back in, which is what a person reads first.
    /// </summary>
    private static string Ordered(Grouping grouping) => grouping switch
    {
        // Most severe first, not most numerous: a breakdown by level is read to
        // find whether anything at the top of the scale is in it, and two Fatal
        // entries under nine thousand Information ones is the answer. By the
        // number and not by the text it was grouped as, which would put ten
        // above nine if the scale ever reached that far.
        Grouping.Level => "min(level) desc",

        // Newest first, as every other read on this surface is. The bucket is
        // rendered fixed-width in UTC, so its text sorts as the instant does.
        Grouping.Time => "1 desc",

        // Largest first, which is the reading a breakdown by logger name or
        // instance is asked for: which part of the application, or which copy of
        // it, is producing this.
        _ => """2 desc, 1""",
    };

    /// <summary>
    /// Runs one statement inside the five seconds, and turns the server's way of
    /// saying so into the layer above's.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>set local</c> and therefore a transaction, because the alternative sets
    /// it on the session and a statement that throws between the setting and the
    /// resetting leaves the next borrower of that pooled connection with somebody
    /// else's limit. A read-only transaction around a single statement costs
    /// nothing worth measuring and it cannot leak.
    /// </para>
    /// <para>
    /// The connection is left as it was found, for the same reason the write is:
    /// a read inside somebody else's open connection does not close it
    /// underneath them.
    /// </para>
    /// </remarks>
    private async Task<IReadOnlyList<TRow>> QueryAsync<TRow>(
        string sql, DynamicParameters parameters, CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)context.Database.GetDbConnection();

        var wasClosed = connection.State is not ConnectionState.Open;
        if (wasClosed)
        {
            await context.Database.OpenConnectionAsync(cancellationToken);
        }

        try
        {
            await using var transaction = await connection.BeginTransactionAsync(
                IsolationLevel.ReadCommitted, cancellationToken);

            await connection.ExecuteAsync(new CommandDefinition(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"set local statement_timeout = {(int)ReadLimit.Duration.TotalMilliseconds}"),
                transaction: transaction,
                cancellationToken: cancellationToken));

            var rows = await connection.QueryAsync<TRow>(new CommandDefinition(
                sql, parameters, transaction, cancellationToken: cancellationToken));

            // Materialized inside the transaction, because Dapper's buffered
            // read is what actually runs the statement and a lazy one would run
            // it after the limit has gone out of scope with it.
            var read = rows.AsList();

            await transaction.CommitAsync(cancellationToken);

            return read;
        }
        catch (PostgresException expired) when (expired.SqlState == QueryCanceled)
        {
            // The server stopped the statement, which on this surface only ever
            // means the five seconds. A caller that went away cancels through the
            // token instead and arrives as an OperationCanceledException, which
            // is not caught here — there is nobody left to tell what to narrow.
            throw new ReadExpiredException(expired);
        }
        finally
        {
            if (wasClosed)
            {
                await context.Database.CloseConnectionAsync();
            }
        }
    }

    /// <summary>Postgres's <c>query_canceled</c>.</summary>
    private const string QueryCanceled = "57014";

    /// <summary>
    /// A position in the arrival order, as it comes back.
    /// </summary>
    /// <remarks>
    /// The two columns of the receipt index and none of the entry: what arms a
    /// tail is where the order currently ends, and reading the row behind it
    /// would be fetching a message and an exception nobody is going to show.
    /// </remarks>
    private sealed record Arrival(DateTime ReceiptTime, long Id);

    /// <summary>
    /// One row of the entry table, as it comes back.
    /// </summary>
    /// <remarks>
    /// It exists so that the mapping from columns to an entry is written down in
    /// one place and checked by the compiler, rather than left to a convention
    /// about names. <see cref="LogEntry"/> validates what it is handed — a trace
    /// that is not sixteen bytes is refused there — and going through this is
    /// what keeps that true of an entry that came out of the database as well as
    /// of one that came off the wire.
    /// </remarks>
    private sealed record Row(
        long Id,
        Guid ProjectId,
        DateTime EventTime,
        DateTime ReceiptTime,

        // The number, because the number is what the column holds — the name is
        // the enum's and putting it back on is the last line of this mapping.
        short LevelNumber,
        string? LoggerName,
        string? Instance,
        byte[]? TraceId,
        byte[]? SpanId,
        string MessageTemplate,
        string RenderedMessage,
        string? Exception,
        string? Properties,
        bool MessageTruncated,
        bool ExceptionTruncated)
    {
        public LogEntry Entry() => new()
        {
            Id = Id,
            ProjectId = ProjectId,

            // `timestamptz` holds no offset of its own and Npgsql hands these
            // back as UTC, so this is the instant that was stored and not a
            // reinterpretation of it.
            EventTime = new DateTimeOffset(EventTime, TimeSpan.Zero),
            ReceiptTime = new DateTimeOffset(ReceiptTime, TimeSpan.Zero),
            Level = (Level)LevelNumber,
            LoggerName = LoggerName,
            Instance = Instance,
            TraceId = TraceId,
            SpanId = SpanId,
            MessageTemplate = MessageTemplate,
            RenderedMessage = RenderedMessage,
            Exception = Exception,
            Properties = Properties,
            MessageTruncated = MessageTruncated,
            ExceptionTruncated = ExceptionTruncated,
        };
    }
}
