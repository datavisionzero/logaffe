using System.Text.Json.Nodes;
using Logaffe.Application.Ports;
using Logaffe.Domain.Entries;
using Logaffe.Infrastructure.Persistence;
using Logaffe.Infrastructure.Persistence.Log;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Logaffe.IntegrationTests;

/// <summary>
/// The write, against the Postgres an installation runs.
/// </summary>
/// <remarks>
/// <para>
/// Binary <c>COPY</c> matches values to columns by position and checks no names,
/// so a value in the wrong column is a mistake nothing in the compiler can catch
/// — it is a batch that stores happily and puts the logger name in the instance.
/// That is the whole reason this is asked of a real table rather than of a
/// substitute, and it is why the round trip below fills every column at once.
/// </para>
/// <para>
/// The counter is here for the same reason: it reads its high-water mark out of
/// the table, so what it does on an installation that already holds entries is a
/// fact about a query and not about arithmetic.
/// </para>
/// </remarks>
[Collection(nameof(PostgresCollection))]
public sealed class EntryWriterTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Now = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_batch_arrives_in_the_columns_it_belongs_in()
    {
        var connectionString = await MigratedAsync();
        var projectId = Guid.CreateVersion7();

        // Everything at once: the required fields, all four promoted
        // properties, the exception, the properties as an object, and both
        // truncation flags — because what would go wrong here is an offset by
        // one column, and only a full row can show it.
        var full = new LogEntry
        {
            Id = 1,
            ProjectId = projectId,
            EventTime = Now,
            ReceiptTime = Now.AddMilliseconds(120),
            Level = Level.Error,
            LoggerName = "Orders.Api.CheckoutController",
            Instance = "api-7c4f",
            TraceId = [.. Enumerable.Range(0, LogEntry.TraceIdLength).Select(b => (byte)b)],
            SpanId = [.. Enumerable.Range(0, LogEntry.SpanIdLength).Select(b => (byte)b)],
            MessageTemplate = "Checkout {OrderId} failed",
            RenderedMessage = "Checkout 4711 failed",
            Exception = "System.IO.IOException: No space left on device\n   at …",
            Properties = """{"UserId":42,"Ip":"203.0.113.7"}""",
            MessageTruncated = true,
            ExceptionTruncated = true,
        };

        // And the other shape a delivery has: a clock and a template, and every
        // column promotion fills left empty.
        var plain = new LogEntry
        {
            Id = 2,
            ProjectId = projectId,
            EventTime = Now,
            ReceiptTime = Now,
            Level = Level.Information,
            MessageTemplate = "Disk full on /dev/sda1",
            RenderedMessage = "Disk full on /dev/sda1",
            MessageTruncated = false,
            ExceptionTruncated = false,
        };

        await using (var context = ContextFor(connectionString))
        {
            await new Entries(context).WriteAsync(
                [full, plain], TestContext.Current.CancellationToken);
        }

        await using var connection = await OpenAsync(connectionString);
        await using var command = new NpgsqlCommand(
            """
            select id, project_id, event_time, receipt_time, level, logger_name, instance,
                   trace_id, span_id, message_template, rendered_message, exception,
                   properties, message_truncated, exception_truncated
            from log_entry
            order by id
            """,
            connection);

        await using var reader = await command.ExecuteReaderAsync(
            TestContext.Current.CancellationToken);

        Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken));
        Assert.Equal(full.Id, reader.GetInt64(0));
        Assert.Equal(full.ProjectId, reader.GetGuid(1));
        Assert.Equal(full.EventTime, reader.GetFieldValue<DateTimeOffset>(2));
        Assert.Equal(full.ReceiptTime, reader.GetFieldValue<DateTimeOffset>(3));
        Assert.Equal((short)full.Level, reader.GetInt16(4));
        Assert.Equal(full.LoggerName, reader.GetString(5));
        Assert.Equal(full.Instance, reader.GetString(6));
        Assert.Equal(full.TraceId, reader.GetFieldValue<byte[]>(7));
        Assert.Equal(full.SpanId, reader.GetFieldValue<byte[]>(8));
        Assert.Equal(full.MessageTemplate, reader.GetString(9));
        Assert.Equal(full.RenderedMessage, reader.GetString(10));
        Assert.Equal(full.Exception, reader.GetString(11));

        // The object, not its text: jsonb keeps no key order and no whitespace,
        // which is all this column has to hold (ADR 0010).
        Assert.True(JsonNode.DeepEquals(
            JsonNode.Parse(full.Properties!), JsonNode.Parse(reader.GetString(12))));
        Assert.True(reader.GetBoolean(13));
        Assert.True(reader.GetBoolean(14));

        Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken));
        Assert.Equal(plain.Id, reader.GetInt64(0));
        Assert.All([5, 6, 7, 8, 11, 12], column => Assert.True(reader.IsDBNull(column)));
        Assert.False(reader.GetBoolean(13));
        Assert.False(reader.GetBoolean(14));
    }

    [Fact]
    public async Task An_event_time_with_an_offset_is_stored_as_the_instant_it_names()
    {
        var connectionString = await MigratedAsync();

        // `timestamptz` holds an instant and no offset of its own, so the two
        // below are one row's worth of the same moment. What must not happen is
        // the offset being dropped rather than applied.
        var noon = new DateTimeOffset(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);
        var elsewhere = new DateTimeOffset(2026, 8, 7, 14, 0, 0, TimeSpan.FromHours(2));

        await using (var context = ContextFor(connectionString))
        {
            await new Entries(context).WriteAsync(
                [Entry(1, noon), Entry(2, elsewhere)], TestContext.Current.CancellationToken);
        }

        await using var connection = await OpenAsync(connectionString);
        await using var command = new NpgsqlCommand(
            "select count(distinct event_time) from log_entry", connection);

        Assert.Equal(1L, await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_batch_that_cannot_be_written_leaves_no_part_of_itself_behind()
    {
        var connectionString = await MigratedAsync();

        await using (var context = ContextFor(connectionString))
        {
            await new Entries(context).WriteAsync(
                [Entry(1, Now)], TestContext.Current.CancellationToken);
        }

        // The second batch collides with what is already there on its last row.
        // A COPY that does not complete rolls back whole, which is what the port
        // promises: the entries of one delivery are in the table or they are not.
        await using (var context = ContextFor(connectionString))
        {
            await Assert.ThrowsAsync<PostgresException>(() => new Entries(context).WriteAsync(
                [Entry(2, Now), Entry(3, Now), Entry(1, Now)],
                TestContext.Current.CancellationToken));
        }

        await using var connection = await OpenAsync(connectionString);
        await using var command = new NpgsqlCommand("select count(*) from log_entry", connection);

        Assert.Equal(1L, await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_counter_starts_above_what_the_table_already_holds()
    {
        var connectionString = await MigratedAsync();

        await using (var context = ContextFor(connectionString))
        {
            await new Entries(context).WriteAsync(
                [Entry(41, Now), Entry(4_711, Now)], TestContext.Current.CancellationToken);
        }

        // Seeded from the high-water mark, so the first identity handed out
        // after a restart is one nothing in the table has — which is the whole
        // of what a counter in memory has to get right.
        var ids = IdsFor(connectionString);

        Assert.Equal(4_712, await ids.ReserveAsync(1, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_counter_of_an_installation_that_has_received_nothing_starts_at_one()
    {
        var ids = IdsFor(await MigratedAsync());

        Assert.Equal(1, await ids.ReserveAsync(1, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Blocks_are_consecutive_and_never_overlap()
    {
        var ids = IdsFor(await MigratedAsync());

        // Handed out concurrently, because an installation is a single writer of
        // a table and not of a process: two deliveries land on two threads, and
        // a block that overlapped another would break the cursor of
        // docs/querying.md rather than merely leave a gap.
        var blocks = await Task.WhenAll(Enumerable.Range(0, 32).Select(
            _ => ids.ReserveAsync(10, TestContext.Current.CancellationToken)));

        Assert.Equal(
            Enumerable.Range(0, 32).Select(index => (long)(index * 10) + 1).Order(),
            blocks.Order());
    }

    private static LogEntry Entry(long id, DateTimeOffset eventTime) => new()
    {
        Id = id,
        ProjectId = Guid.CreateVersion7(),
        EventTime = eventTime,
        ReceiptTime = Now,
        Level = Level.Information,
        MessageTemplate = "Disk full on /dev/sda1",
        RenderedMessage = "Disk full on /dev/sda1",
        MessageTruncated = false,
        ExceptionTruncated = false,
    };

    /// <summary>
    /// The counter as the composition root builds it: a singleton that opens a
    /// scope of its own to read the mark.
    /// </summary>
    private static IEntryIds IdsFor(string connectionString) =>
        new ServiceCollection()
            .AddDbContext<LogaffeDbContext>(options => options.UseNpgsql(connectionString))
            .AddSingleton<IEntryIds, EntryIds>()
            .BuildServiceProvider()
            .GetRequiredService<IEntryIds>();

    private async Task<string> MigratedAsync()
    {
        var connectionString = await postgres.CreateDatabaseAsync();

        await using var context = ContextFor(connectionString);
        await new SchemaMigrator(context, NullLogger<SchemaMigrator>.Instance)
            .ApplyAsync(TestContext.Current.CancellationToken);

        return connectionString;
    }

    private static async Task<NpgsqlConnection> OpenAsync(string connectionString)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        return connection;
    }

    private static LogaffeDbContext ContextFor(string connectionString) =>
        new(new DbContextOptionsBuilder<LogaffeDbContext>().UseNpgsql(connectionString).Options);
}
