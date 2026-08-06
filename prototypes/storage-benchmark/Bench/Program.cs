namespace Bench;

using System.Text;

/// PROTOTYPE — throwaway. See ../README.md for the question this answers.
static class Program
{
    static async Task<int> Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("commands: reset | load | index | sizes | query | retention | churn | stage");
            return 1;
        }

        var options = ParseOptions(args.Skip(1));
        var command = args[0];

        try
        {
            switch (command)
            {
                case "reset": await ResetAsync(options); break;
                case "load": await LoadAsync(options); break;
                case "index": await IndexAsync(options); break;
                case "sizes": await SizesAsync(); break;
                case "query": await QueryAsync(options); break;
                case "retention": await RetentionAsync(options); break;
                case "churn": await ChurnAsync(options); break;
                case "stage": await StageAsync(options); break;
                default:
                    Console.Error.WriteLine($"unknown command '{command}'");
                    return 1;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"FAILED: {ex.Message}");
            return 1;
        }

        return 0;
    }

    // ---- commands -----------------------------------------------------------

    static async Task ResetAsync(Options o)
    {
        await using var conn = await Db.OpenAsync();
        Console.WriteLine($"reset (index set: {o.Indexes})");
        await Db.ExecAsync(conn, "create extension if not exists pgstattuple");
        await Schema.ResetAsync(conn);
        await Meta.SetAsync(conn, "indexes", o.Indexes);
        await Meta.SetAsync(conn, "projects", o.Projects.ToString());
    }

    static async Task LoadAsync(Options o)
    {
        await using var conn = await Db.OpenAsync();
        var to = DateTime.UtcNow;
        var from = to.AddDays(-o.Days);
        var corpus = new Corpus(o.Projects);

        Console.WriteLine(
            $"load {o.Entries:N0} entries across {o.Projects} projects over {o.Days} days " +
            $"(batch {o.BatchSize}, {o.Writers} writers)");

        var result = await Loader.LoadAsync(corpus, o.Entries, from, to, o.BatchSize, o.Writers);

        Console.WriteLine(
            $"  {result.Entries:N0} entries in {result.Elapsed.TotalSeconds:F1}s " +
            $"= {result.EntriesPerSecond:N0} entries/s");
        Console.WriteLine($"  per batch: p50 {result.BatchP50Ms:F1}ms  p95 {result.BatchP95Ms:F1}ms");

        await Meta.SetAsync(conn, "entries", o.Entries.ToString());
        await Meta.SetAsync(conn, "days", o.Days.ToString());
        await Meta.SetAsync(conn, "load_rate", result.EntriesPerSecond.ToString("F0"));
        await Db.ExecAsync(conn, "analyze log_entry");
    }

    static async Task IndexAsync(Options o)
    {
        await using var conn = await Db.OpenAsync();
        Console.WriteLine($"building index set '{o.Indexes}'");
        var elapsed = await Schema.CreateIndexesAsync(conn, o.Indexes);
        Console.WriteLine($"  total {elapsed.TotalSeconds:F1}s");
        await Db.ExecAsync(conn, "analyze log_entry");
    }

    static async Task SizesAsync()
    {
        await using var conn = await Db.OpenAsync();
        Stats.Print(await Stats.SnapshotAsync(conn));
    }

    static async Task QueryAsync(Options o)
    {
        await using var conn = await Db.OpenAsync();
        var projects = await Meta.ProjectsAsync(conn);
        Console.WriteLine($"query suite, {o.Iterations} iterations each");
        await Queries.RunAsync(conn, projects[0], o.Iterations);
    }

    static async Task RetentionAsync(Options o)
    {
        await using var conn = await Db.OpenAsync();
        var projects = await Meta.ProjectsAsync(conn);

        var before = await Stats.SnapshotAsync(conn);
        var cutoff = DateTime.UtcNow.AddDays(-o.KeepDays);
        Console.WriteLine($"retention sweep: dropping entries received before {cutoff:u} (batch {o.Batch})");

        var result = await Retention.SweepAsync(conn, projects, cutoff, o.Batch);
        Console.WriteLine(
            $"  deleted {result.Deleted:N0} rows in {result.Elapsed.TotalSeconds:F1}s " +
            $"= {result.RowsPerSecond:N0} rows/s");
        Console.WriteLine($"  per batch: p50 {result.BatchP50Ms:F1}ms  p95 {result.BatchP95Ms:F1}ms");

        Console.WriteLine("\n  --- immediately after the sweep ---");
        Stats.Print(await Stats.SnapshotAsync(conn));

        Console.WriteLine("\n  running vacuum (analyze) ...");
        var watch = System.Diagnostics.Stopwatch.StartNew();
        await Db.ExecAsync(conn, "vacuum (analyze) log_entry");
        Console.WriteLine($"  vacuum took {watch.Elapsed.TotalSeconds:F1}s");

        Console.WriteLine("\n  --- after vacuum ---");
        var after = await Stats.SnapshotAsync(conn);
        Stats.Print(after);
        Console.WriteLine(
            $"\n  space returned to the OS: " +
            $"{(before.TotalBytes - after.TotalBytes) / (1024.0 * 1024):F0} MiB " +
            $"of {(before.TotalBytes) / (1024.0 * 1024):F0} MiB");
    }

    static async Task ChurnAsync(Options o)
    {
        await using var conn = await Db.OpenAsync();
        var projects = await Meta.ProjectsAsync(conn);
        var corpus = new Corpus(projects.Count);

        Console.WriteLine(
            $"churn for {o.Minutes} minutes: {o.Writers} writers ingesting while retention " +
            $"keeps a {o.RetentionMinutes}-minute window");

        await Retention.ChurnAsync(
            corpus, projects, TimeSpan.FromMinutes(o.Minutes),
            TimeSpan.FromMinutes(o.RetentionMinutes), o.BatchSize, o.Writers, o.Batch);
    }

    /// One stage = reset, load, index, size, query, sweep. The whole answer for
    /// one volume, in one command.
    static async Task StageAsync(Options o)
    {
        // Iterations fall as the volume rises: search_two_chars is a deliberate
        // full scan (ADR 0010), and at stress volume it would otherwise be the
        // only thing this command measures.
        var (entries, projects, days, iterations) = o.Stage switch
        {
            "smoke" => (1_000_000L, 10, 7, 25),
            "target" => (10_000_000L, 20, 30, 8),
            "stress" => (25_000_000L, 30, 90, 4),
            _ => throw new ArgumentException("stage must be smoke, target or stress"),
        };

        var stageOptions = o with
        {
            Entries = entries,
            Projects = projects,
            Days = days,
            Iterations = iterations,
        };
        var log = new StringBuilder();
        var writer = new StringWriter(log);
        var console = Console.Out;
        var tee = new TeeWriter(console, writer);
        Console.SetOut(tee);

        Console.WriteLine($"# Stage: {o.Stage}");
        Console.WriteLine($"entries {entries:N0}, projects {projects}, days {days}, index set {o.Indexes}\n");

        Console.WriteLine("## Load with indexes already in place\n");
        await ResetAsync(stageOptions);
        await IndexAsync(stageOptions);
        await LoadAsync(stageOptions);

        Console.WriteLine("\n## Sizes\n");
        await SizesAsync();

        Console.WriteLine("\n## Queries\n");
        await QueryAsync(stageOptions);

        Console.WriteLine("\n## Retention sweep (drop the oldest third)\n");
        await RetentionAsync(stageOptions with { KeepDays = days * 2 / 3 });

        Console.SetOut(console);

        var directory = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "results");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{DateTime.Now:yyyy-MM-dd-HHmm}-{o.Stage}-{o.Indexes}.md");
        await File.WriteAllTextAsync(path, log.ToString());
        Console.WriteLine($"\nwritten to {Path.GetFullPath(path)}");
    }

    // ---- options ------------------------------------------------------------

    sealed record Options
    {
        public string Indexes { get; init; } = "full";
        public string Stage { get; init; } = "smoke";
        public long Entries { get; init; } = 1_000_000;
        public int Projects { get; init; } = 20;
        public int Days { get; init; } = 30;
        public int BatchSize { get; init; } = 500;
        public int Writers { get; init; } = 4;
        public int Iterations { get; init; } = 25;
        public int KeepDays { get; init; } = 20;
        public int Batch { get; init; } = 20_000;
        public int Minutes { get; init; } = 20;
        public int RetentionMinutes { get; init; } = 5;
    }

    static Options ParseOptions(IEnumerable<string> args)
    {
        var options = new Options();
        var list = args.ToList();
        for (var i = 0; i < list.Count - 1; i += 2)
        {
            var key = list[i].TrimStart('-');
            var value = list[i + 1];
            options = key switch
            {
                "indexes" => options with { Indexes = value },
                "stage" => options with { Stage = value },
                "entries" => options with { Entries = long.Parse(value) },
                "projects" => options with { Projects = int.Parse(value) },
                "days" => options with { Days = int.Parse(value) },
                "batch-size" => options with { BatchSize = int.Parse(value) },
                "writers" => options with { Writers = int.Parse(value) },
                "iterations" => options with { Iterations = int.Parse(value) },
                "keep-days" => options with { KeepDays = int.Parse(value) },
                "batch" => options with { Batch = int.Parse(value) },
                "minutes" => options with { Minutes = int.Parse(value) },
                "retention-minutes" => options with { RetentionMinutes = int.Parse(value) },
                _ => options,
            };
        }
        return options;
    }
}

sealed class TeeWriter(TextWriter first, TextWriter second) : TextWriter
{
    public override Encoding Encoding => first.Encoding;

    public override void Write(char value)
    {
        first.Write(value);
        second.Write(value);
    }

    public override void Write(string? value)
    {
        first.Write(value);
        second.Write(value);
    }

    public override void WriteLine(string? value)
    {
        first.WriteLine(value);
        second.WriteLine(value);
    }
}
