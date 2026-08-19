using System.Data;
using System.Globalization;
using Dapper;
using Logaffe.Application.Ports;
using Logaffe.Domain.Hosts;
using Logaffe.Domain.Queries;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Logaffe.Infrastructure.Persistence;

/// <summary>
/// Reading what a host reported, bucketed on the way out.
/// </summary>
/// <remarks>
/// <para>
/// One grouped statement over the leading columns of <c>pk_host_sample</c>, and
/// a second over <c>pk_filesystem_reading</c>. The bucketing is done here rather
/// than by the caller because the alternative is ten thousand rows crossing a
/// layer boundary on their way to being averaged.
/// </para>
/// <para>
/// <b>The statements are written out rather than composed in LINQ</b>, which is
/// the entry reader's arrangement for a different reason. There it is because
/// the statements are fitted to indexes whose cost <c>docs/storage.md</c>
/// measures; here it is because the grouping expression is arithmetic on an
/// instant — how many spans past the start of the range a reading falls — and no
/// provider translates that. Written out, the group is
/// <c>extract(epoch from …)</c> and the plan is a range scan over the key; left
/// to LINQ, it is a query that does not compile to SQL at all.
/// </para>
/// <para>
/// <b>It gets the same five seconds every other read on this surface gets</b>
/// (ADR 0026), set the same way: <c>statement_timeout</c> on the server, so that
/// the read that meets it is stopped by the database rather than abandoned by a
/// client still waiting on it. Nothing measured here comes near five seconds —
/// these tables are three orders of magnitude smaller than the entries — so what
/// the limit bounds is the range nobody anticipated.
/// </para>
/// </remarks>
public sealed class SampleReader(LogaffeDbContext context) : ISampleReader
{
    /// <summary>
    /// Which span a reading falls in, as the span's own start.
    /// </summary>
    /// <remarks>
    /// <c>date_bin</c> rather than arithmetic on an epoch, which is what makes
    /// the group and the answer the same value: the bucket comes back as the
    /// instant it begins at, so nothing multiplies an index back into a time and
    /// nothing can drift between the two. The origin is the start of the range,
    /// so the first span opens exactly where the caller asked rather than
    /// wherever a fixed epoch happens to put it.
    /// </remarks>
    private const string Bucket = """date_bin(@Span, receipt_time, @From)""";

    public async Task<SampleWindow> ReadAsync(
        Guid hostId,
        DateTimeOffset from,
        DateTimeOffset to,
        BucketCount buckets,
        CancellationToken cancellationToken)
    {
        // The range divided by the count, floored to the microsecond the store
        // keeps. A range shorter than the count gives every span one
        // microsecond, which is harmless: what comes back is the samples that
        // exist, and there are at most a handful in a range that short.
        var span = TimeSpan.FromTicks(
            Math.Max(10, ((to - from).Ticks / buckets.Value / 10) * 10));

        var samples = await QueryAsync<SampleRow>(
            $"""
             select {Bucket} as "Start",
                    avg(cpu)::double precision as "CpuAverage",
                    max(cpu)::double precision as "CpuPeak",
                    avg(memory_used)::double precision as "MemoryUsedAverage",
                    max(memory_used) as "MemoryUsedPeak",
                    max(memory_total) as "MemoryTotal",
                    avg(load_1)::double precision as "LoadAverage",
                    max(load_1)::double precision as "LoadPeak"
             from host_sample
             where host_id = @HostId and receipt_time >= @From and receipt_time <= @To
             group by 1
             order by 1
             """,
            new { HostId = hostId, From = from, To = to, Span = span },
            cancellationToken);

        var filesystems = await QueryAsync<FilesystemRow>(
            $"""
             select {Bucket} as "Start",
                    mount_path as "MountPath",
                    avg(used)::double precision as "UsedAverage",
                    max(used) as "UsedPeak",
                    max(total) as "Total"
             from filesystem_reading
             where host_id = @HostId and receipt_time >= @From and receipt_time <= @To
             group by 1, 2
             order by 1, 2
             """,
            new { HostId = hostId, From = from, To = to, Span = span },
            cancellationToken);

        return new SampleWindow(
            [
                .. samples.Select(row => new SampleBucket(
                    Instant(row.Start),
                    row.CpuAverage,
                    row.CpuPeak,
                    (long)row.MemoryUsedAverage,
                    row.MemoryUsedPeak,
                    row.MemoryTotal,
                    row.LoadAverage,
                    row.LoadPeak)),
            ],
            [
                .. filesystems.Select(row => new FilesystemBucket(
                    Instant(row.Start),
                    MountPath.Create(row.MountPath),
                    (long)row.UsedAverage,
                    row.UsedPeak,
                    row.Total)),
            ]);
    }

    /// <remarks>
    /// One grouped statement rather than one lookup per host: samples are not
    /// scoped the way entries are (ADR 0045), so unlike the project list's
    /// equivalent there is no per-host reader standing in the way of asking for
    /// all of them at once.
    /// </remarks>
    public async Task<IReadOnlyDictionary<Guid, DateTimeOffset>> LastReportedAsync(
        CancellationToken cancellationToken) =>
        await context.Samples
            .AsNoTracking()
            .GroupBy(s => s.HostId)
            .Select(host => new { HostId = host.Key, Last = host.Max(s => s.ReceiptTime) })
            .ToDictionaryAsync(row => row.HostId, row => row.Last, cancellationToken);

    /// <summary>
    /// A span's start as an instant.
    /// </summary>
    /// <remarks>
    /// <c>timestamptz</c> holds no offset of its own and Npgsql hands these back
    /// as UTC, so this is the instant that was stored and not a reinterpretation
    /// of it.
    /// </remarks>
    private static DateTimeOffset Instant(DateTime start) => new(start, TimeSpan.Zero);

    /// <summary>
    /// One statement under the five seconds, on the connection EF Core is
    /// holding.
    /// </summary>
    /// <remarks>
    /// The connection is left as it was found, for the reason the entry reader
    /// leaves it: this may be inside a scope that opened it, and closing
    /// somebody else's connection is a bug that only shows up under load.
    /// </remarks>
    private async Task<IReadOnlyList<TRow>> QueryAsync<TRow>(
        string sql, object parameters, CancellationToken cancellationToken)
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
    /// One span of the machine's own numbers, as it comes back.
    /// </summary>
    /// <remarks>
    /// The start arrives as a <see cref="DateTime"/> because that is what Npgsql
    /// reads a <c>timestamptz</c> back as; <see cref="Instant"/> is where it
    /// becomes the moment it was.
    /// </remarks>
    private sealed record SampleRow(
        DateTime Start,
        double CpuAverage,
        double CpuPeak,
        double MemoryUsedAverage,
        long MemoryUsedPeak,
        long MemoryTotal,
        double LoadAverage,
        double LoadPeak);

    /// <summary>One span of one filesystem, as it comes back.</summary>
    /// <inheritdoc cref="SampleRow"/>
    private sealed record FilesystemRow(
        DateTime Start, string MountPath, double UsedAverage, long UsedPeak, long Total);
}
