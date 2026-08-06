namespace Bench;

using System.Diagnostics;
using Npgsql;
using NpgsqlTypes;

/// PROTOTYPE. The write path of ADR 0003: Npgsql binary COPY, one COPY per
/// delivered batch, because that is how the ingestion endpoint will use it.
static class Loader
{
    const string CopyCommand = """
        copy log_entry (
            id, project_id, event_time, receipt_time, level,
            logger_name, instance, trace_id, span_id,
            message_template, rendered_message, exception, properties, truncation
        ) from stdin (format binary)
        """;

    public sealed record Result(long Entries, TimeSpan Elapsed, double BatchP50Ms, double BatchP95Ms)
    {
        public double EntriesPerSecond => Entries / Elapsed.TotalSeconds;
    }

    public static async Task<Result> LoadAsync(
        Corpus corpus, long entries, DateTime from, DateTime to, int batchSize, int writers)
    {
        var queue = new Queue<List<Corpus.Entry>>();
        var current = new List<Corpus.Entry>(batchSize);
        foreach (var entry in corpus.Generate(entries, from, to))
        {
            current.Add(entry);
            if (current.Count == batchSize)
            {
                queue.Enqueue(current);
                current = new List<Corpus.Entry>(batchSize);
            }
        }
        if (current.Count > 0) queue.Enqueue(current);

        var batches = new System.Collections.Concurrent.ConcurrentQueue<List<Corpus.Entry>>(queue);
        var latencies = new System.Collections.Concurrent.ConcurrentBag<double>();
        var total = new Stopwatch();

        total.Start();
        var tasks = Enumerable.Range(0, writers).Select(async _ =>
        {
            await using var conn = await Db.OpenAsync();
            while (batches.TryDequeue(out var batch))
            {
                var watch = Stopwatch.StartNew();
                await WriteBatchAsync(conn, batch);
                latencies.Add(watch.Elapsed.TotalMilliseconds);
            }
        });
        await Task.WhenAll(tasks);
        total.Stop();

        var sorted = latencies.OrderBy(x => x).ToArray();
        return new Result(
            entries,
            total.Elapsed,
            Percentile(sorted, 0.50),
            Percentile(sorted, 0.95));
    }

    public static async Task WriteBatchAsync(NpgsqlConnection conn, IReadOnlyList<Corpus.Entry> batch)
    {
        await using var writer = await conn.BeginBinaryImportAsync(CopyCommand);
        foreach (var e in batch)
        {
            await writer.StartRowAsync();
            await writer.WriteAsync(e.Id, NpgsqlDbType.Bigint);
            await writer.WriteAsync(e.ProjectId, NpgsqlDbType.Uuid);
            await writer.WriteAsync(e.EventTime, NpgsqlDbType.TimestampTz);
            await writer.WriteAsync(e.ReceiptTime, NpgsqlDbType.TimestampTz);
            await writer.WriteAsync(e.Level, NpgsqlDbType.Smallint);
            await WriteNullableAsync(writer, e.LoggerName, NpgsqlDbType.Text);
            await WriteNullableAsync(writer, e.Instance, NpgsqlDbType.Text);
            await WriteNullableAsync(writer, e.TraceId, NpgsqlDbType.Text);
            await WriteNullableAsync(writer, e.SpanId, NpgsqlDbType.Text);
            await writer.WriteAsync(e.MessageTemplate, NpgsqlDbType.Text);
            await writer.WriteAsync(e.RenderedMessage, NpgsqlDbType.Text);
            await WriteNullableAsync(writer, e.Exception, NpgsqlDbType.Text);
            await WriteNullableAsync(writer, e.Properties, NpgsqlDbType.Jsonb);
            await writer.WriteAsync((short)0, NpgsqlDbType.Smallint);
        }
        await writer.CompleteAsync();
    }

    static async Task WriteNullableAsync(NpgsqlBinaryImporter writer, string? value, NpgsqlDbType type)
    {
        if (value is null) await writer.WriteNullAsync();
        else await writer.WriteAsync(value, type);
    }

    public static double Percentile(double[] sorted, double p)
    {
        if (sorted.Length == 0) return 0;
        var index = (int)Math.Ceiling(p * sorted.Length) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Length - 1)];
    }
}
