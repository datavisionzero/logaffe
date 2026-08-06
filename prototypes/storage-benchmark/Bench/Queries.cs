namespace Bench;

using System.Diagnostics;
using System.Text;
using Npgsql;
using NpgsqlTypes;

/// PROTOTYPE. Every query shape docs/querying.md promises, timed. If one of
/// these is not interactive at target volume, the document is writing a cheque
/// the schema cannot cash.
static class Queries
{
    public sealed record Case(string Name, string Description, string Sql, Action<NpgsqlCommand, Ctx> Bind);

    public sealed record Ctx(Guid ProjectId, DateTime Now, DateTime CursorTime, long CursorId, DateTime TailCursor);

    public sealed record Measurement(string Name, double P50Ms, double P95Ms, double MaxMs, long Rows, string Plan);

    const string Columns =
        "id, event_time, receipt_time, level, logger_name, instance, rendered_message, exception, properties";

    public static readonly Case[] Cases =
    [
        new("page_first", "newest page, last 24h",
            $"""
             select {Columns} from log_entry
             where project_id = @project and event_time >= @from
             order by event_time desc, id desc
             limit 100
             """,
            (cmd, c) =>
            {
                cmd.Parameters.AddWithValue("project", NpgsqlDbType.Uuid, c.ProjectId);
                cmd.Parameters.AddWithValue("from", NpgsqlDbType.TimestampTz, c.Now.AddDays(-1));
            }),

        new("page_deep", "page reached by cursor, deep into the project",
            $"""
             select {Columns} from log_entry
             where project_id = @project and (event_time, id) < (@ct, @cid)
             order by event_time desc, id desc
             limit 100
             """,
            (cmd, c) =>
            {
                cmd.Parameters.AddWithValue("project", NpgsqlDbType.Uuid, c.ProjectId);
                cmd.Parameters.AddWithValue("ct", NpgsqlDbType.TimestampTz, c.CursorTime);
                cmd.Parameters.AddWithValue("cid", NpgsqlDbType.Bigint, c.CursorId);
            }),

        new("page_warning_and_above", "level threshold, last 7 days",
            $"""
             select {Columns} from log_entry
             where project_id = @project and event_time >= @from and level >= 3
             order by event_time desc, id desc
             limit 100
             """,
            (cmd, c) =>
            {
                cmd.Parameters.AddWithValue("project", NpgsqlDbType.Uuid, c.ProjectId);
                cmd.Parameters.AddWithValue("from", NpgsqlDbType.TimestampTz, c.Now.AddDays(-7));
            }),

        new("page_logger", "one logger name, last 7 days",
            $"""
             select {Columns} from log_entry
             where project_id = @project and logger_name = @logger and event_time >= @from
             order by event_time desc, id desc
             limit 100
             """,
            (cmd, c) =>
            {
                cmd.Parameters.AddWithValue("project", NpgsqlDbType.Uuid, c.ProjectId);
                cmd.Parameters.AddWithValue("logger", NpgsqlDbType.Text, "Acme.Orders.OrderService");
                cmd.Parameters.AddWithValue("from", NpgsqlDbType.TimestampTz, c.Now.AddDays(-7));
            }),

        new("search_phrase", "substring 'failed login' over the whole project",
            $"""
             select {Columns} from log_entry
             where project_id = @project and rendered_message ilike @needle
             order by event_time desc, id desc
             limit 100
             """,
            (cmd, c) =>
            {
                cmd.Parameters.AddWithValue("project", NpgsqlDbType.Uuid, c.ProjectId);
                cmd.Parameters.AddWithValue("needle", NpgsqlDbType.Text, "%failed login%");
            }),

        new("search_identifier", "substring '203.0.113.42' — the shape people actually type",
            $"""
             select {Columns} from log_entry
             where project_id = @project and rendered_message ilike @needle
             order by event_time desc, id desc
             limit 100
             """,
            (cmd, c) =>
            {
                cmd.Parameters.AddWithValue("project", NpgsqlDbType.Uuid, c.ProjectId);
                cmd.Parameters.AddWithValue("needle", NpgsqlDbType.Text, "%203.0.113.42%");
            }),

        new("search_no_match", "substring that matches nothing — the index has to rule out every candidate",
            $"""
             select {Columns} from log_entry
             where project_id = @project and rendered_message ilike @needle
             order by event_time desc, id desc
             limit 100
             """,
            (cmd, c) =>
            {
                cmd.Parameters.AddWithValue("project", NpgsqlDbType.Uuid, c.ProjectId);
                cmd.Parameters.AddWithValue("needle", NpgsqlDbType.Text, "%NoSuchTokenAnywhere%");
            }),

        new("search_two_chars", "sub-trigram search with no match — index-ineligible by design, so a scan (ADR 0010)",
            $"""
             select {Columns} from log_entry
             where project_id = @project and rendered_message ilike @needle
             order by event_time desc, id desc
             limit 100
             """,
            (cmd, c) =>
            {
                cmd.Parameters.AddWithValue("project", NpgsqlDbType.Uuid, c.ProjectId);
                cmd.Parameters.AddWithValue("needle", NpgsqlDbType.Text, "%qz%");
            }),

        new("tail_poll", "the five-second poll: what arrived since last time (ADR 0009)",
            $"""
             select {Columns} from log_entry
             where project_id = @project and receipt_time > @cursor
             order by receipt_time, id
             limit 200
             """,
            (cmd, c) =>
            {
                cmd.Parameters.AddWithValue("project", NpgsqlDbType.Uuid, c.ProjectId);
                cmd.Parameters.AddWithValue("cursor", NpgsqlDbType.TimestampTz, c.TailCursor);
            }),

        new("count_by_level", "count grouped by level, last 3 days — VISION.md's example question",
            """
            select level, count(*) from log_entry
            where project_id = @project and event_time >= @from
            group by level
            """,
            (cmd, c) =>
            {
                cmd.Parameters.AddWithValue("project", NpgsqlDbType.Uuid, c.ProjectId);
                cmd.Parameters.AddWithValue("from", NpgsqlDbType.TimestampTz, c.Now.AddDays(-3));
            }),

        new("count_search", "count over a substring filter — the expensive count",
            """
            select count(*) from log_entry
            where project_id = @project and rendered_message ilike @needle
            """,
            (cmd, c) =>
            {
                cmd.Parameters.AddWithValue("project", NpgsqlDbType.Uuid, c.ProjectId);
                cmd.Parameters.AddWithValue("needle", NpgsqlDbType.Text, "%failed login%");
            }),
    ];

    public static async Task<List<Measurement>> RunAsync(NpgsqlConnection conn, Guid busiestProject, int iterations)
    {
        var ctx = await BuildContextAsync(conn, busiestProject);
        var results = new List<Measurement>();

        foreach (var testCase in Cases)
        {
            var samples = new List<double>();
            long rows = 0;

            // One warm-up run, then measure steady state.
            for (var i = 0; i < iterations + 1; i++)
            {
                var watch = Stopwatch.StartNew();
                rows = await ExecuteAsync(conn, testCase, ctx);
                watch.Stop();
                if (i > 0) samples.Add(watch.Elapsed.TotalMilliseconds);
            }

            var sorted = samples.OrderBy(x => x).ToArray();
            var plan = await ExplainAsync(conn, testCase, ctx);
            var measurement = new Measurement(
                testCase.Name,
                Loader.Percentile(sorted, 0.50),
                Loader.Percentile(sorted, 0.95),
                sorted.LastOrDefault(),
                rows,
                plan);

            results.Add(measurement);
            Console.WriteLine(
                $"  {measurement.Name,-24} p50 {measurement.P50Ms,9:F1}ms  p95 {measurement.P95Ms,9:F1}ms  rows {rows,7}");
        }

        return results;
    }

    static async Task<long> ExecuteAsync(NpgsqlConnection conn, Case testCase, Ctx ctx)
    {
        await using var cmd = new NpgsqlCommand(testCase.Sql, conn);
        testCase.Bind(cmd, ctx);
        await using var reader = await cmd.ExecuteReaderAsync();
        long rows = 0;
        while (await reader.ReadAsync()) rows++;
        return rows;
    }

    static async Task<string> ExplainAsync(NpgsqlConnection conn, Case testCase, Ctx ctx)
    {
        await using var cmd = new NpgsqlCommand("explain (analyze, buffers, timing) " + testCase.Sql, conn);
        testCase.Bind(cmd, ctx);
        await using var reader = await cmd.ExecuteReaderAsync();
        var plan = new StringBuilder();
        while (await reader.ReadAsync()) plan.AppendLine(reader.GetString(0));
        return plan.ToString();
    }

    static async Task<Ctx> BuildContextAsync(NpgsqlConnection conn, Guid project)
    {
        var now = await Db.ScalarAsync<DateTime>(conn,
            $"select max(receipt_time) from log_entry where project_id = '{project}'");

        // A cursor 5 000 entries into the project — a page nobody reaches by
        // accident, and the one an offset would have made expensive.
        await using var cmd = new NpgsqlCommand(
            """
            select event_time, id from log_entry
            where project_id = @project
            order by event_time desc, id desc
            offset 5000 limit 1
            """, conn);
        cmd.Parameters.AddWithValue("project", NpgsqlDbType.Uuid, project);
        await using var reader = await cmd.ExecuteReaderAsync();

        var cursorTime = now;
        long cursorId = long.MaxValue;
        if (await reader.ReadAsync())
        {
            cursorTime = reader.GetDateTime(0);
            cursorId = reader.GetInt64(1);
        }

        return new Ctx(project, now, cursorTime, cursorId, now.AddSeconds(-30));
    }
}
