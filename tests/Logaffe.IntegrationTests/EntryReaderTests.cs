using Logaffe.Application.Ports;
using Logaffe.Domain.Entries;
using Logaffe.Domain.Queries;
using Logaffe.Infrastructure.Persistence;
using Logaffe.Infrastructure.Persistence.Log;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Logaffe.IntegrationTests;

/// <summary>
/// The filtered page, the count and one entry, against the Postgres an
/// installation runs.
/// </summary>
/// <remarks>
/// <para>
/// These statements are hand-written because they are fitted to the indexes
/// <c>docs/storage.md</c> claims (ADR 0003), which means nothing in the compiler
/// vouches for them: a filter that narrows to the wrong rows, an order that puts
/// the cursor at the wrong end of a page, or a substring search that reads a
/// percent sign as syntax are all mistakes that compile. So every one of them is
/// asked of a real table.
/// </para>
/// <para>
/// The entries go in through the ingestion path's own <c>COPY</c> writer,
/// because that is what put them there in production and a row inserted another
/// way is a row that proves less.
/// </para>
/// </remarks>
[Collection(nameof(PostgresCollection))]
public sealed class EntryReaderTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Ten = new(2026, 8, 8, 10, 0, 0, TimeSpan.Zero);

    private readonly Guid _project = Guid.CreateVersion7();
    private readonly Guid _other = Guid.CreateVersion7();

    [Fact]
    public async Task A_page_is_newest_first_by_event_time()
    {
        var reader = await ReadingAsync(
            Entry(1, Ten.AddMinutes(-5)),
            Entry(2, Ten),
            Entry(3, Ten.AddMinutes(-10)));

        var page = await PageAsync(reader);

        // Event time and not receipt time, and not the identity either: the
        // order is the one the log happened in (ADR 0007).
        Assert.Equal([2, 1, 3], page.Select(entry => entry.Id));
    }

    [Fact]
    public async Task Entries_sharing_an_event_time_are_ordered_by_identity()
    {
        // The ordinary case rather than a contrived one — one batch, one
        // millisecond — and the reason the cursor carries the identity at all.
        var reader = await ReadingAsync(Entry(1, Ten), Entry(2, Ten), Entry(3, Ten));

        var page = await PageAsync(reader);

        Assert.Equal([3, 2, 1], page.Select(entry => entry.Id));
    }

    [Fact]
    public async Task A_cursor_resumes_after_the_entry_it_names_and_not_at_it()
    {
        var reader = await ReadingAsync(
            Entry(1, Ten), Entry(2, Ten.AddMinutes(-1)), Entry(3, Ten.AddMinutes(-2)));

        var page = await PageAsync(reader, after: new EntryCursor(Ten, 1));

        // Neither repeating the entry the cursor names nor skipping the one
        // after it, which is the whole of what paging by cursor has to get right.
        Assert.Equal([2, 3], page.Select(entry => entry.Id));
    }

    [Fact]
    public async Task A_cursor_steps_through_entries_that_share_an_event_time()
    {
        var reader = await ReadingAsync(Entry(1, Ten), Entry(2, Ten), Entry(3, Ten));

        // The half of the pair that is not the timestamp doing its job: a cursor
        // on the event time alone would either return all three again or none of
        // them.
        var page = await PageAsync(reader, after: new EntryCursor(Ten, 3));

        Assert.Equal([2, 1], page.Select(entry => entry.Id));
    }

    [Fact]
    public async Task A_page_holds_at_most_what_a_page_holds()
    {
        var reader = await ReadingAsync(
            [.. Enumerable.Range(1, Page.Size + 20).Select(id => Entry(id, Ten.AddSeconds(-id)))]);

        Assert.Equal(Page.Size, (await PageAsync(reader)).Count);
    }

    [Fact]
    public async Task A_read_never_leaves_the_project_it_was_asked_for()
    {
        var reader = await ReadingAsync(
            Entry(1, Ten), Entry(2, Ten, projectId: _other), Entry(3, Ten));

        var page = await PageAsync(reader);
        var counted = await CountAsync(reader);

        // Not a permission but an absence: the project leads every index on this
        // table and every statement here begins with it.
        Assert.Equal([3, 1], page.Select(entry => entry.Id));
        Assert.Equal(2, Assert.Single(counted).Entries);
    }

    [Fact]
    public async Task A_time_range_is_half_open_and_reads_the_event_clock()
    {
        var reader = await ReadingAsync(
            Entry(1, Ten.AddMinutes(-1)),
            Entry(2, Ten),
            Entry(3, Ten.AddMinutes(5)),
            Entry(4, Ten.AddMinutes(10)));

        var page = await PageAsync(reader, new EntryFilters
        {
            From = Ten,
            Until = Ten.AddMinutes(10),
        });

        // The start is in and the end is out, so that consecutive ranges neither
        // overlap nor leave a gap.
        Assert.Equal([3, 2], page.Select(entry => entry.Id));
    }

    [Fact]
    public async Task A_level_is_a_threshold_and_not_a_selection()
    {
        var reader = await ReadingAsync(
            Entry(1, Ten, level: Level.Debug),
            Entry(2, Ten, level: Level.Warning),
            Entry(3, Ten, level: Level.Error),
            Entry(4, Ten, level: Level.Fatal));

        var page = await PageAsync(reader, new EntryFilters { MinimumLevel = Level.Warning });

        // "Warning and above" is the question people actually ask, and it is the
        // predicate the partial index of docs/storage.md is defined over.
        Assert.Equal([4, 3, 2], page.Select(entry => entry.Id));
    }

    [Fact]
    public async Task An_instance_and_a_logger_name_are_matched_whole()
    {
        var reader = await ReadingAsync(
            Entry(1, Ten, loggerName: "Orders.Api.Checkout", instance: "api-7c4f"),
            Entry(2, Ten, loggerName: "Orders.Api", instance: "api-7c4f"),
            Entry(3, Ten, loggerName: "Orders.Api", instance: "api-91ab"));

        var byLogger = await PageAsync(reader, new EntryFilters { LoggerName = "Orders.Api" });
        var byInstance = await PageAsync(reader, new EntryFilters { Instance = "api-7c4f" });

        // Whole rather than by prefix: `Microsoft.*` is a grammar in one column,
        // which is the thing ADR 0011 declined.
        Assert.Equal([3, 2], byLogger.Select(entry => entry.Id));
        Assert.Equal([2, 1], byInstance.Select(entry => entry.Id));
    }

    [Fact]
    public async Task A_trace_gathers_the_entries_of_one_request()
    {
        var trace = Bytes(LogEntry.TraceIdLength, 1);
        var another = Bytes(LogEntry.TraceIdLength, 2);

        var reader = await ReadingAsync(
            Entry(1, Ten, traceId: trace),
            Entry(2, Ten, traceId: another),
            Entry(3, Ten, traceId: trace));

        var page = await PageAsync(reader, new EntryFilters { TraceId = trace });

        Assert.Equal([3, 1], page.Select(entry => entry.Id));
    }

    [Fact]
    public async Task Search_is_grep_and_not_a_search_engine()
    {
        var reader = await ReadingAsync(
            Entry(1, Ten, message: "Checkout 4711 failed for /api/orders/4711"),
            Entry(2, Ten, message: "Connected to 203.0.113.7"),
            Entry(3, Ten, message: "REQUEST TIMEOUT after 30s"),
            Entry(4, Ten, message: "Request timed out"));

        // Case-insensitive, anywhere in the message, and inside a word — the
        // searches an operator actually types, which a word-based full-text
        // index would tokenize apart or not find at all (ADR 0010).
        Assert.Equal([1], await IdsAsync(reader, "api/orders/4711"));
        Assert.Equal([2], await IdsAsync(reader, "203.0.113.7"));
        Assert.Equal([3], await IdsAsync(reader, "timeout"));
        Assert.Equal([4, 3], await IdsAsync(reader, "req"));
    }

    [Fact]
    public async Task A_search_for_a_character_like_reads_as_syntax_finds_that_character()
    {
        var reader = await ReadingAsync(
            Entry(1, Ten, message: "Disk 100% full"),
            Entry(2, Ten, message: "Disk 100 percent full"),
            Entry(3, Ten, message: "Read a_b from cache"),
            Entry(4, Ten, message: "Read axb from cache"));

        // Without escaping, `100%` would match the second as well and `a_b` the
        // fourth — an operator would be told their filter found things it did
        // not. The substring promise has to hold for the characters the pattern
        // language happens to use.
        Assert.Equal([1], await IdsAsync(reader, "100%"));
        Assert.Equal([3], await IdsAsync(reader, "a_b"));
    }

    [Fact]
    public async Task The_exception_is_searched_on_its_own_and_the_message_is_not()
    {
        var reader = await ReadingAsync(
            Entry(1, Ten, message: "Order 4711 failed",
                exception: "System.NullReferenceException: Object reference not set\n   at …"),
            Entry(2, Ten, message: "NullReferenceException in handler"));

        var byException = await PageAsync(
            reader, new EntryFilters { ExceptionText = SearchText.Create("nullreference") });
        var byMessage = await PageAsync(
            reader, new EntryFilters { Search = SearchText.Create("nullreference") });

        // The product's own motivating search: in a normal .NET application the
        // exception type is in the exception and not in the sentence, which is
        // why the filter is separate (ADR 0028). The two do not reach into each
        // other's column.
        Assert.Equal([1], byException.Select(entry => entry.Id));
        Assert.Equal([2], byMessage.Select(entry => entry.Id));
    }

    [Fact]
    public async Task Filters_set_together_all_apply()
    {
        var reader = await ReadingAsync(
            Entry(1, Ten, level: Level.Error, loggerName: "Orders.Api", message: "Checkout failed"),
            Entry(2, Ten, level: Level.Debug, loggerName: "Orders.Api", message: "Checkout failed"),
            Entry(3, Ten, level: Level.Error, loggerName: "Billing.Api", message: "Checkout failed"),
            Entry(4, Ten, level: Level.Error, loggerName: "Orders.Api", message: "Checkout started"),
            Entry(5, Ten.AddDays(-1), level: Level.Error, loggerName: "Orders.Api",
                message: "Checkout failed"));

        var page = await PageAsync(reader, new EntryFilters
        {
            From = Ten.AddMinutes(-5),
            Until = Ten.AddMinutes(5),
            MinimumLevel = Level.Warning,
            LoggerName = "Orders.Api",
            Search = SearchText.Create("failed"),
        });

        // Every one of them removes entries and none adds any, and they combine
        // with AND alone (ADR 0011).
        Assert.Equal([1], page.Select(entry => entry.Id));
    }

    [Fact]
    public async Task An_ungrouped_count_is_one_number_over_the_same_filters()
    {
        var reader = await ReadingAsync(
            Entry(1, Ten, level: Level.Error),
            Entry(2, Ten, level: Level.Information),
            Entry(3, Ten, level: Level.Fatal));

        var counted = await CountAsync(reader, new EntryFilters { MinimumLevel = Level.Warning });

        var group = Assert.Single(counted);
        Assert.Null(group.Value);
        Assert.Equal(2, group.Entries);
    }

    [Fact]
    public async Task A_count_grouped_by_level_comes_back_most_severe_first()
    {
        var reader = await ReadingAsync(
            Entry(1, Ten, level: Level.Information),
            Entry(2, Ten, level: Level.Information),
            Entry(3, Ten, level: Level.Information),
            Entry(4, Ten, level: Level.Fatal));

        var counted = await CountAsync(reader, grouping: Grouping.Level);

        // Most severe and not most numerous: one Fatal under three Information
        // entries is the answer somebody asked this question for.
        Assert.Equal(
            [((short)Level.Fatal).ToString(), ((short)Level.Information).ToString()],
            counted.Select(group => group.Value));
        Assert.Equal([1, 3], counted.Select(group => group.Entries));
    }

    [Fact]
    public async Task A_count_grouped_by_logger_name_comes_back_largest_first()
    {
        var reader = await ReadingAsync(
            Entry(1, Ten, loggerName: "Orders.Api"),
            Entry(2, Ten, loggerName: "Billing.Api"),
            Entry(3, Ten, loggerName: "Billing.Api"));

        var counted = await CountAsync(reader, grouping: Grouping.LoggerName);

        // Which part of the application is noisy, which is the reading this is
        // asked for.
        Assert.Equal(["Billing.Api", "Orders.Api"], counted.Select(group => group.Value));
        Assert.Equal([2, 1], counted.Select(group => group.Entries));
    }

    [Fact]
    public async Task The_entries_carrying_no_value_are_a_group_and_not_a_disappearance()
    {
        var reader = await ReadingAsync(
            Entry(1, Ten, loggerName: "Orders.Api"),
            Entry(2, Ten),
            Entry(3, Ten));

        var counted = await CountAsync(reader, grouping: Grouping.LoggerName);

        // Promotion asks nothing of a sender, so entries without a logger name
        // are ordinary. A grouped count whose rows did not add up to the plain
        // one would be a number nobody could act on.
        Assert.Equal([null, "Orders.Api"], counted.Select(group => group.Value));
        Assert.Equal(3, counted.Sum(group => group.Entries));
    }

    [Fact]
    public async Task A_time_bucket_is_aligned_to_the_clock_and_not_to_the_range()
    {
        var reader = await ReadingAsync(
            Entry(1, Ten.AddMinutes(5)),
            Entry(2, Ten.AddMinutes(59)),
            Entry(3, Ten.AddHours(1)));

        var counted = await CountAsync(
            reader,
            new EntryFilters { From = Ten.AddMinutes(3) },
            Grouping.Time,
            TimeBucket.Hour);

        // Aligned to the hour rather than to 10:03, so the same entry falls in
        // the same bucket whatever window it is counted in — which is what makes
        // two counts of overlapping ranges comparable. Newest first, as every
        // read here is.
        Assert.Equal(
            ["2026-08-08T11:00:00Z", "2026-08-08T10:00:00Z"],
            counted.Select(group => group.Value));
        Assert.Equal([1, 2], counted.Select(group => group.Entries));
    }

    [Fact]
    public async Task One_entry_comes_back_whole()
    {
        var stored = new LogEntry
        {
            Id = 12,
            ProjectId = _project,
            EventTime = Ten,
            ReceiptTime = Ten.AddMilliseconds(120),
            Level = Level.Error,
            LoggerName = "Orders.Api.CheckoutController",
            Instance = "api-7c4f",
            TraceId = Bytes(LogEntry.TraceIdLength, 1),
            SpanId = Bytes(LogEntry.SpanIdLength, 2),
            MessageTemplate = "Checkout {OrderId} failed",
            RenderedMessage = "Checkout 4711 failed",
            Exception = "System.IO.IOException: No space left on device\n   at …",
            Properties = """{"UserId": 42, "Ip": "203.0.113.7"}""",
            MessageTruncated = true,
            ExceptionTruncated = true,
        };

        var reader = await ReadingAsync(stored);

        var read = await reader.FindAsync(_project, 12, TestContext.Current.CancellationToken);

        // Every column, because this is the follow-up after a compact search and
        // what is wanted is exactly the fields a page does not carry. A value
        // landing in the neighbouring property is a mistake nothing else here
        // would catch.
        Assert.NotNull(read);
        Assert.Equal(stored.Id, read.Id);
        Assert.Equal(stored.ProjectId, read.ProjectId);
        Assert.Equal(stored.EventTime, read.EventTime);
        Assert.Equal(stored.ReceiptTime, read.ReceiptTime);
        Assert.Equal(stored.Level, read.Level);
        Assert.Equal(stored.LoggerName, read.LoggerName);
        Assert.Equal(stored.Instance, read.Instance);
        Assert.Equal(stored.TraceId, read.TraceId);
        Assert.Equal(stored.SpanId, read.SpanId);
        Assert.Equal(stored.MessageTemplate, read.MessageTemplate);
        Assert.Equal(stored.RenderedMessage, read.RenderedMessage);
        Assert.Equal(stored.Exception, read.Exception);
        Assert.True(read.MessageTruncated);
        Assert.True(read.ExceptionTruncated);
    }

    [Fact]
    public async Task An_entry_is_not_reachable_from_a_project_it_does_not_belong_to()
    {
        var reader = await ReadingAsync(Entry(12, Ten));

        // The identity is unique, so this is not about finding the row. It is
        // about the separation holding on the read path as well: guessing a
        // number from another project must not open one.
        Assert.NotNull(await reader.FindAsync(
            _project, 12, TestContext.Current.CancellationToken));
        Assert.Null(await reader.FindAsync(
            _other, 12, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Every_read_is_cut_off_after_five_seconds()
    {
        var connectionString = await MigratedAsync();
        await WriteAsync(connectionString, Entry(1, Ten));

        // The statements are the ones being timed, so what is made slow is the
        // table underneath them: the real one is put aside and a view that
        // sleeps takes its name. `pg_sleep` is volatile, so it is evaluated per
        // row rather than folded away.
        await ExecuteAsync(
            connectionString,
            """
            alter table log_entry rename to log_entry_stored;
            create view log_entry as
                select * from log_entry_stored where length(pg_sleep(30)::text) >= 0;
            """);

        await using var context = ContextFor(connectionString);
        var reader = new EntryReader(context);

        // Cut off by the server rather than by a token this waited on — a
        // cancelled wait leaves the statement running, and an installation
        // occupied by one request is the thing ADR 0026 exists to prevent.
        await Assert.ThrowsAsync<ReadExpiredException>(() => PageAsync(reader));
        await Assert.ThrowsAsync<ReadExpiredException>(() => CountAsync(reader));
        await Assert.ThrowsAsync<ReadExpiredException>(() =>
            reader.FindAsync(_project, 1, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_limit_a_read_runs_under_is_its_own_and_not_the_databases()
    {
        var connectionString = await MigratedAsync();
        await WriteAsync(connectionString, Entry(1, Ten));

        // An installation whose database was handed a strict default. The five
        // seconds are one value for every read in every installation, so the
        // read sets its own rather than inheriting whatever it found — and this
        // is the direction that would otherwise go unnoticed, because a limit
        // that is never reached looks like a limit that works.
        var database = new NpgsqlConnectionStringBuilder(connectionString).Database;
        await ExecuteAsync(
            connectionString, $"""alter database "{database}" set statement_timeout = '1ms'""");

        // A new connection, because a database default is read when one is
        // opened. The fixture pools none, so this is one.
        await using var context = ContextFor(connectionString);

        Assert.Single(await PageAsync(new EntryReader(context)));
    }

    private static byte[] Bytes(int length, byte seed) =>
        [.. Enumerable.Range(0, length).Select(i => (byte)(seed + i))];

    private LogEntry Entry(
        long id,
        DateTimeOffset at,
        Guid? projectId = null,
        Level level = Level.Information,
        string? loggerName = null,
        string? instance = null,
        byte[]? traceId = null,
        string? message = null,
        string? exception = null) => new()
    {
        Id = id,
        ProjectId = projectId ?? _project,
        EventTime = at,
        ReceiptTime = at,
        Level = level,
        LoggerName = loggerName,
        Instance = instance,
        TraceId = traceId,
        MessageTemplate = message ?? "Checkout {OrderId} failed",
        RenderedMessage = message ?? $"Checkout {id} failed",
        Exception = exception,
        MessageTruncated = false,
        ExceptionTruncated = false,
    };

    private Task<IReadOnlyList<LogEntry>> PageAsync(
        IEntryReader reader, EntryFilters? filters = null, EntryCursor? after = null) =>
        reader.PageAsync(
            _project, filters ?? EntryFilters.None, after, TestContext.Current.CancellationToken);

    private async Task<IReadOnlyList<long>> IdsAsync(IEntryReader reader, string search)
    {
        var page = await PageAsync(
            reader, new EntryFilters { Search = SearchText.Create(search) });

        return [.. page.Select(entry => entry.Id)];
    }

    private Task<IReadOnlyList<CountedGroup>> CountAsync(
        IEntryReader reader,
        EntryFilters? filters = null,
        Grouping grouping = Grouping.None,
        TimeBucket bucket = TimeBucket.Hour) =>
        reader.CountAsync(
            _project,
            filters ?? EntryFilters.None,
            grouping,
            bucket,
            TestContext.Current.CancellationToken);

    /// <summary>
    /// A migrated database holding <paramref name="entries"/>, and a reader over
    /// it.
    /// </summary>
    private async Task<IEntryReader> ReadingAsync(params LogEntry[] entries)
    {
        var connectionString = await MigratedAsync();
        await WriteAsync(connectionString, entries);

        return new EntryReader(ContextFor(connectionString));
    }

    private async Task<string> MigratedAsync()
    {
        var connectionString = await postgres.CreateDatabaseAsync();

        await using var context = ContextFor(connectionString);
        await new SchemaMigrator(context, NullLogger<SchemaMigrator>.Instance)
            .ApplyAsync(TestContext.Current.CancellationToken);

        return connectionString;
    }

    private static async Task WriteAsync(string connectionString, params LogEntry[] entries)
    {
        if (entries.Length == 0)
        {
            return;
        }

        await using var context = ContextFor(connectionString);
        await new Entries(context).WriteAsync(entries, TestContext.Current.CancellationToken);
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private static LogaffeDbContext ContextFor(string connectionString) =>
        new(new DbContextOptionsBuilder<LogaffeDbContext>()
            .UseNpgsql(connectionString)
            .Options);
}
