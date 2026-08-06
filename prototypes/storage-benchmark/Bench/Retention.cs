namespace Bench;

using System.Diagnostics;
using Npgsql;
using NpgsqlTypes;

/// PROTOTYPE. ADR 0023 chose row deletion over partition dropping and named the
/// GIN index under insert-and-delete churn as the risk it accepted. This is the
/// measurement that decides whether that acceptance was justified.
static class Retention
{
    /// Deleting by ctid keeps the sweep independent of whether the table has a
    /// primary key, which is itself one of the things under test.
    const string SweepBatch = """
        delete from log_entry
        where ctid in (
            select ctid from log_entry
            where project_id = @project and receipt_time < @cutoff
            order by receipt_time
            limit @batch
        )
        """;

    public sealed record SweepResult(long Deleted, TimeSpan Elapsed, double BatchP50Ms, double BatchP95Ms)
    {
        public double RowsPerSecond => Elapsed.TotalSeconds == 0 ? 0 : Deleted / Elapsed.TotalSeconds;
    }

    public static async Task<SweepResult> SweepAsync(
        NpgsqlConnection conn, IReadOnlyList<Guid> projects, DateTime cutoff, int batch)
    {
        var latencies = new List<double>();
        long deleted = 0;
        var total = Stopwatch.StartNew();

        foreach (var project in projects)
        {
            while (true)
            {
                await using var cmd = new NpgsqlCommand(SweepBatch, conn);
                cmd.Parameters.AddWithValue("project", NpgsqlDbType.Uuid, project);
                cmd.Parameters.AddWithValue("cutoff", NpgsqlDbType.TimestampTz, cutoff);
                cmd.Parameters.AddWithValue("batch", NpgsqlDbType.Integer, batch);

                var watch = Stopwatch.StartNew();
                var affected = await cmd.ExecuteNonQueryAsync();
                watch.Stop();

                if (affected == 0) break;
                deleted += affected;
                latencies.Add(watch.Elapsed.TotalMilliseconds);
            }
        }

        total.Stop();
        var sorted = latencies.OrderBy(x => x).ToArray();
        return new SweepResult(deleted, total.Elapsed, Loader.Percentile(sorted, 0.50), Loader.Percentile(sorted, 0.95));
    }

    public sealed record ChurnSample(
        TimeSpan At, long Entries, long HeapBytes, long TrigramBytes, long DeadTuples,
        long? GinPendingPages, double PageP50Ms, double SearchP50Ms, double IngestPerSecond);

    /// Steady state: entries arriving continuously while the sweep removes the
    /// expired ones, which is the only condition in which the GIN index's
    /// behaviour is actually visible.
    public static async Task<List<ChurnSample>> ChurnAsync(
        Corpus corpus, IReadOnlyList<Guid> projects, TimeSpan duration,
        TimeSpan retentionWindow, int batchSize, int writers, int sweepBatch)
    {
        var samples = new List<ChurnSample>();
        var stop = new CancellationTokenSource(duration);
        var ingested = 0L;
        var started = Stopwatch.StartNew();

        var ingestTask = Task.Run(async () =>
        {
            var tasks = Enumerable.Range(0, writers).Select(async _ =>
            {
                await using var conn = await Db.OpenAsync();
                while (!stop.IsCancellationRequested)
                {
                    var now = DateTime.UtcNow;
                    var batch = corpus.Generate(batchSize, now, now.AddSeconds(1)).ToList();
                    try { await Loader.WriteBatchAsync(conn, batch); }
                    catch (OperationCanceledException) { break; }
                    Interlocked.Add(ref ingested, batch.Count);
                }
            });
            await Task.WhenAll(tasks);
        });

        var sweepTask = Task.Run(async () =>
        {
            await using var conn = await Db.OpenAsync();
            while (!stop.IsCancellationRequested)
            {
                await SweepAsync(conn, projects, DateTime.UtcNow - retentionWindow, sweepBatch);
                try { await Task.Delay(TimeSpan.FromSeconds(15), stop.Token); }
                catch (TaskCanceledException) { break; }
            }
        });

        await using var probe = await Db.OpenAsync();
        var lastIngested = 0L;
        var lastAt = TimeSpan.Zero;

        while (!stop.IsCancellationRequested)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(30), stop.Token); }
            catch (TaskCanceledException) { break; }

            var snapshot = await Stats.SnapshotAsync(probe);
            var trigram = snapshot.Indexes.FirstOrDefault(i => i.Name == "ix_log_entry_trgm")?.Bytes ?? 0;
            var project = projects[0];

            var pageMs = await TimeAsync(probe,
                """
                select id, event_time, rendered_message from log_entry
                where project_id = @project
                order by event_time desc, id desc limit 100
                """, project, null);
            var searchMs = await TimeAsync(probe,
                """
                select id, event_time, rendered_message from log_entry
                where project_id = @project and rendered_message ilike @needle
                order by event_time desc, id desc limit 100
                """, project, "%failed login%");

            var at = started.Elapsed;
            var currentIngested = Interlocked.Read(ref ingested);
            var rate = (currentIngested - lastIngested) / Math.Max(1, (at - lastAt).TotalSeconds);
            lastIngested = currentIngested;
            lastAt = at;

            var sample = new ChurnSample(
                at, snapshot.Entries, snapshot.HeapBytes, trigram, snapshot.DeadTuples,
                snapshot.GinPendingPages, pageMs, searchMs, rate);
            samples.Add(sample);

            Console.WriteLine(
                $"  t+{at.TotalMinutes,5:F1}m  entries {sample.Entries,11:N0}  " +
                $"trgm {sample.TrigramBytes / (1024.0 * 1024):F0}MiB  dead {sample.DeadTuples,9:N0}  " +
                $"pending {sample.GinPendingPages,6}  page {sample.PageP50Ms,7:F1}ms  " +
                $"search {sample.SearchP50Ms,8:F1}ms  in {sample.IngestPerSecond,8:F0}/s");
        }

        await Task.WhenAll(ingestTask, sweepTask);
        return samples;
    }

    static async Task<double> TimeAsync(NpgsqlConnection conn, string sql, Guid project, string? needle)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("project", NpgsqlDbType.Uuid, project);
        if (needle is not null) cmd.Parameters.AddWithValue("needle", NpgsqlDbType.Text, needle);

        var watch = Stopwatch.StartNew();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) { }
        return watch.Elapsed.TotalMilliseconds;
    }
}
