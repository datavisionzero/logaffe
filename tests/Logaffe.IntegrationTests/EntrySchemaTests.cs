using System.Text.Json.Nodes;
using Logaffe.Domain.Entries;
using Logaffe.Domain.Projects;
using Logaffe.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Logaffe.IntegrationTests;

/// <summary>
/// The entry table against a real Postgres.
/// </summary>
/// <remarks>
/// <para>
/// <c>docs/storage.md</c> is a set of claims about specific index definitions,
/// each with a measured size and a query it exists for, and an index that is
/// silently one column short still answers every one of those queries — slowly,
/// at ten million rows, on the host nobody is watching. So the definitions are
/// read back out of the catalog rather than off the configuration that was
/// supposed to produce them.
/// </para>
/// <para>
/// The entries themselves are written with binary <c>COPY</c> and read with
/// hand-written SQL (ADR 0003), so there is no <c>DbSet</c> to reach them
/// through and nothing here pretends otherwise: the rows below are put in by
/// hand, because what is being asked about is the table.
/// </para>
/// </remarks>
[Collection(nameof(PostgresCollection))]
public sealed class EntrySchemaTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Now = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Every index <c>docs/storage.md</c> writes out, as Postgres renders it
    /// back. The order of the columns and the direction of each one are the
    /// whole point — the paging index is the cursor of <c>docs/querying.md</c>,
    /// and the receipt index is the tail and the sweep.
    /// </summary>
    private static readonly (string Name, string Definition)[] Claimed =
    [
        ("pk_log_entry", "USING btree (id)"),
        ("ix_log_entry_paging", "USING btree (project_id, event_time DESC, id DESC)"),
        ("ix_log_entry_receipt", "USING btree (project_id, receipt_time, id)"),
        ("ix_log_entry_logger_name", "USING btree (project_id, logger_name, event_time DESC)"),
        ("ix_log_entry_instance", "USING btree (project_id, instance, event_time DESC)"),
        ("ix_log_entry_trace", "USING btree (project_id, trace_id, event_time DESC)"),
        ("ix_log_entry_warning_and_above",
            "USING btree (project_id, event_time DESC, id DESC) WHERE (level >= 3)"),
        ("ix_log_entry_search", "USING gin (project_id, rendered_message gin_trgm_ops)"),
    ];

    public static TheoryData<string, string> ClaimedIndexes()
    {
        var data = new TheoryData<string, string>();
        foreach (var (name, definition) in Claimed)
        {
            data.Add(name, definition);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(ClaimedIndexes))]
    public async Task The_index_is_what_storage_md_says_it_is(string name, string definition)
    {
        await using var connection = await MigratedAsync();

        var actual = await ScalarAsync<string>(
            connection,
            "select indexdef from pg_indexes where tablename = 'log_entry' and indexname = @name",
            ("name", name));

        Assert.EndsWith(definition, actual, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_table_carries_the_indexes_it_claims_and_no_others()
    {
        await using var connection = await MigratedAsync();

        var found = await ListAsync<string>(
            connection, "select indexname from pg_indexes where tablename = 'log_entry'");

        // Stated in both directions on purpose. The indexes together are larger
        // than the table, which is affordable only because each one is there for
        // a named query — an extra one is a cost nobody decided to pay, on the
        // hottest write path in the product.
        Assert.Equal(Claimed.Select(index => index.Name).Order(), found.Order());
    }

    [Fact]
    public async Task Autovacuum_is_configured_rather_than_left_alone()
    {
        await using var connection = await MigratedAsync();

        var options = await ScalarAsync<string[]>(
            connection, "select reloptions from pg_class where relname = 'log_entry'");

        // ADR 0023 requires it: a table where a predictable fraction expires
        // every day is the wrong shape for a default that waits until a fifth of
        // it is dead.
        Assert.Equal(
            [
                "autovacuum_vacuum_scale_factor=0.01",
                "autovacuum_vacuum_threshold=20000",
                "autovacuum_vacuum_cost_limit=2000",
                "autovacuum_analyze_scale_factor=0.02",
                "autovacuum_vacuum_insert_scale_factor=0.02",
            ],
            options);
    }

    [Fact]
    public async Task An_entry_outlives_the_project_row_it_belongs_to()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        var project = Project.Create("orders-api", RetentionWindow.OfDays(14), Now);

        await using (var context = ContextFor(connectionString))
        {
            await MigratorFor(context).ApplyAsync(TestContext.Current.CancellationToken);
            context.Projects.Add(project);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var connection = await OpenAsync(connectionString);
        await StoreAsync(connection, Entry(project.Id));

        await using (var context = ContextFor(connectionString))
        {
            context.Projects.Remove(project);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        // A project is deleted at once and its entries follow afterwards, in the
        // background (ADR 0019). There is no foreign key here deliberately: a
        // cascade would put millions of rows back inside the request the
        // operator is standing in front of. The row that is left is unreachable
        // — every query runs inside a project, and this one is gone.
        Assert.Equal(
            1L,
            await ScalarAsync<long>(connection, "select count(*) from log_entry"));
    }

    [Fact]
    public async Task An_entry_round_trips_through_the_columns_it_is_stored_in()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        var project = Project.Create("orders-api", RetentionWindow.OfDays(14), Now);

        await using (var context = ContextFor(connectionString))
        {
            await MigratorFor(context).ApplyAsync(TestContext.Current.CancellationToken);
            context.Projects.Add(project);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        // Everything at once: the required fields, all four promoted properties,
        // the exception, the properties as an object, and both truncation flags.
        var entry = new LogEntry
        {
            Id = 1,
            ProjectId = project.Id,
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

        await using var connection = await OpenAsync(connectionString);
        await StoreAsync(connection, entry);

        await using var command = new NpgsqlCommand(
            """
            select id, project_id, event_time, receipt_time, level, logger_name, instance,
                   trace_id, span_id, message_template, rendered_message, exception,
                   properties, message_truncated, exception_truncated
            from log_entry
            """,
            connection);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken));

        Assert.Equal(entry.Id, reader.GetInt64(0));
        Assert.Equal(entry.ProjectId, reader.GetGuid(1));
        Assert.Equal(entry.EventTime, reader.GetFieldValue<DateTimeOffset>(2));
        Assert.Equal(entry.ReceiptTime, reader.GetFieldValue<DateTimeOffset>(3));
        // A smallint in the column, so that a threshold is a comparison.
        Assert.Equal((short)entry.Level, reader.GetInt16(4));
        Assert.Equal(entry.LoggerName, reader.GetString(5));
        Assert.Equal(entry.Instance, reader.GetString(6));
        // The bytes, not the thirty-two and sixteen hex characters CLEF carries.
        Assert.Equal(entry.TraceId, reader.GetFieldValue<byte[]>(7));
        Assert.Equal(entry.SpanId, reader.GetFieldValue<byte[]>(8));
        Assert.Equal(entry.MessageTemplate, reader.GetString(9));
        Assert.Equal(entry.RenderedMessage, reader.GetString(10));
        Assert.Equal(entry.Exception, reader.GetString(11));
        // The object, not its text. jsonb keeps no key order and no whitespace,
        // so what comes back is the same properties written another way — which
        // is all this column has to hold, because nothing indexes them and no
        // filter reaches inside them (ADR 0010).
        Assert.True(JsonNode.DeepEquals(
            JsonNode.Parse(entry.Properties!), JsonNode.Parse(reader.GetString(12))));
        Assert.True(reader.GetBoolean(13));
        Assert.True(reader.GetBoolean(14));
    }

    private static LogEntry Entry(Guid projectId) => new()
    {
        Id = 1,
        ProjectId = projectId,
        EventTime = Now,
        ReceiptTime = Now.AddMilliseconds(120),
        Level = Level.Error,
        MessageTemplate = "Checkout {OrderId} failed",
        RenderedMessage = "Checkout 4711 failed",
        MessageTruncated = false,
        ExceptionTruncated = false,
    };

    /// <summary>
    /// One row, by hand. The binary <c>COPY</c> that will do this in volume is
    /// the ingestion path's and is not what these tests are asking about.
    /// </summary>
    private static async Task StoreAsync(NpgsqlConnection connection, LogEntry entry)
    {
        await using var command = new NpgsqlCommand(
            """
            insert into log_entry (
                id, project_id, event_time, receipt_time, level, logger_name, instance,
                trace_id, span_id, message_template, rendered_message, exception,
                properties, message_truncated, exception_truncated)
            values (
                @id, @project_id, @event_time, @receipt_time, @level, @logger_name, @instance,
                @trace_id, @span_id, @message_template, @rendered_message, @exception,
                @properties::jsonb, @message_truncated, @exception_truncated)
            """,
            connection);

        command.Parameters.AddWithValue("id", entry.Id);
        command.Parameters.AddWithValue("project_id", entry.ProjectId);
        command.Parameters.AddWithValue("event_time", entry.EventTime);
        command.Parameters.AddWithValue("receipt_time", entry.ReceiptTime);
        command.Parameters.AddWithValue("level", (short)entry.Level);
        command.Parameters.AddWithValue("logger_name", (object?)entry.LoggerName ?? DBNull.Value);
        command.Parameters.AddWithValue("instance", (object?)entry.Instance ?? DBNull.Value);
        command.Parameters.AddWithValue("trace_id", (object?)entry.TraceId ?? DBNull.Value);
        command.Parameters.AddWithValue("span_id", (object?)entry.SpanId ?? DBNull.Value);
        command.Parameters.AddWithValue("message_template", entry.MessageTemplate);
        command.Parameters.AddWithValue("rendered_message", entry.RenderedMessage);
        command.Parameters.AddWithValue("exception", (object?)entry.Exception ?? DBNull.Value);
        command.Parameters.AddWithValue("properties", (object?)entry.Properties ?? DBNull.Value);
        command.Parameters.AddWithValue("message_truncated", entry.MessageTruncated);
        command.Parameters.AddWithValue("exception_truncated", entry.ExceptionTruncated);

        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private async Task<NpgsqlConnection> MigratedAsync()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using (var context = ContextFor(connectionString))
        {
            await MigratorFor(context).ApplyAsync(TestContext.Current.CancellationToken);
        }

        return await OpenAsync(connectionString);
    }

    private static async Task<NpgsqlConnection> OpenAsync(string connectionString)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        return connection;
    }

    private static async Task<T> ScalarAsync<T>(
        NpgsqlConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken));
        return reader.GetFieldValue<T>(0);
    }

    private static async Task<List<T>> ListAsync<T>(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);

        var values = new List<T>();
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            values.Add(reader.GetFieldValue<T>(0));
        }

        return values;
    }

    private static LogaffeDbContext ContextFor(string connectionString) =>
        new(new DbContextOptionsBuilder<LogaffeDbContext>().UseNpgsql(connectionString).Options);

    private static SchemaMigrator MigratorFor(LogaffeDbContext context) =>
        new(context, NullLogger<SchemaMigrator>.Instance);
}
