using Logaffe.Application.Operations;
using Logaffe.Domain.Entries;
using Logaffe.Domain.Projects;
using Logaffe.Infrastructure.Persistence;
using Logaffe.Infrastructure.Persistence.Log;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Logaffe.IntegrationTests;

/// <summary>
/// The sweep against a real Postgres.
/// </summary>
/// <remarks>
/// The statements are hand-written and fitted to the receipt index (ADR 0003),
/// so which rows they take and which they leave is exactly the thing no
/// substitute can vouch for. What the act decides — the cutoff per project, and
/// when it has asked enough times — is asked of the act, in the unit tests.
/// </remarks>
[Collection(nameof(PostgresCollection))]
public sealed class RetentionSweepTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Now = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task An_entry_inside_the_window_stays_and_one_outside_it_goes()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var context = await MigratedAsync(connectionString);

        var project = Project.Create("api", RetentionWindow.OfDays(7), Now.AddDays(-30));
        context.Projects.Add(project);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        await using var connection = await OpenAsync(connectionString);
        // Six days old and eight, either side of a seven-day window.
        await StoreAsync(connection, Entry(1, project.Id, Now.AddDays(-6)));
        await StoreAsync(connection, Entry(2, project.Id, Now.AddDays(-8)));

        await SweepAsync(context);

        Assert.Equal([1L], await IdsAsync(connection));
    }

    [Fact]
    public async Task The_clock_it_counts_from_is_the_receipt_and_not_the_event()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var context = await MigratedAsync(connectionString);

        var project = Project.Create("api", RetentionWindow.OfDays(7), Now.AddDays(-30));
        context.Projects.Add(project);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        await using var connection = await OpenAsync(connectionString);
        // A sender whose clock is a year out, both ways. Neither entry may be
        // decided by what it says about itself (ADR 0007): expiring by the
        // sender's clock would let one keep its rows forever and lose the other
        // on arrival.
        await StoreAsync(
            connection, Entry(1, project.Id, Now.AddDays(-1), happened: Now.AddYears(-1)));
        await StoreAsync(
            connection, Entry(2, project.Id, Now.AddDays(-9), happened: Now.AddYears(1)));

        await SweepAsync(context);

        Assert.Equal([1L], await IdsAsync(connection));
    }

    [Fact]
    public async Task One_project_being_swept_leaves_another_alone()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var context = await MigratedAsync(connectionString);

        var brief = Project.Create("api", RetentionWindow.OfDays(7), Now.AddDays(-30));
        var patient = Project.Create("web", RetentionWindow.OfDays(90), Now.AddDays(-30));
        context.Projects.AddRange(brief, patient);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        await using var connection = await OpenAsync(connectionString);
        await StoreAsync(connection, Entry(1, brief.Id, Now.AddDays(-30)));
        await StoreAsync(connection, Entry(2, patient.Id, Now.AddDays(-30)));

        await SweepAsync(context);

        // Retention is per project, which is the whole reason these are rows
        // being deleted rather than partitions being dropped (ADR 0023).
        Assert.Equal([2L], await IdsAsync(connection));
    }

    [Fact]
    public async Task The_entries_of_a_deleted_project_are_taken_whole()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var context = await MigratedAsync(connectionString);

        var project = Project.Create("api", RetentionWindow.OfDays(90), Now.AddDays(-30));
        context.Projects.Add(project);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        await using var connection = await OpenAsync(connectionString);
        // Well inside the ninety days the project kept, so nothing about the
        // window would have removed it.
        await StoreAsync(connection, Entry(1, project.Id, Now.AddMinutes(-1)));

        context.Projects.Remove(project);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        await SweepAsync(context);

        // A project goes at once and its entries follow in the background
        // (ADR 0019). This is that background — nothing else would ever reach
        // them, because the row that named their window is gone.
        Assert.Empty(await IdsAsync(connection));
    }

    [Fact]
    public async Task A_pass_with_nothing_to_do_removes_nothing()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var context = await MigratedAsync(connectionString);

        var project = Project.Create("api", RetentionWindow.OfDays(7), Now.AddDays(-30));
        context.Projects.Add(project);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        await using var connection = await OpenAsync(connectionString);
        await StoreAsync(connection, Entry(1, project.Id, Now));

        // The ordinary case, hour after hour.
        await SweepAsync(context);
        await SweepAsync(context);

        Assert.Equal([1L], await IdsAsync(connection));
    }

    [Fact]
    public async Task A_portion_takes_the_rows_it_is_bounded_to_and_stops()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var context = await MigratedAsync(connectionString);

        var project = Project.Create("api", RetentionWindow.OfDays(7), Now.AddDays(-30));
        context.Projects.Add(project);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        await using var connection = await OpenAsync(connectionString);
        for (var id = 1; id <= 5; id++)
        {
            await StoreAsync(connection, Entry(id, project.Id, Now.AddDays(-8)));
        }

        // The bound the act repeats against, asked directly: the statement takes
        // a portion and no more, and answers with what it took.
        var entries = new Entries(context);
        var removed = await entries.RemoveReceivedBeforeAsync(
            project.Id, Now, portion: 2, TestContext.Current.CancellationToken);

        Assert.Equal(2, removed);
        Assert.Equal(3, (await IdsAsync(connection)).Count);
    }

    [Fact]
    public async Task The_table_says_which_projects_it_still_holds_rows_for()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var context = await MigratedAsync(connectionString);

        var project = Project.Create("api", RetentionWindow.OfDays(7), Now.AddDays(-30));
        context.Projects.Add(project);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var deleted = Guid.CreateVersion7();
        await using var connection = await OpenAsync(connectionString);
        await StoreAsync(connection, Entry(1, project.Id, Now));
        await StoreAsync(connection, Entry(2, project.Id, Now));
        await StoreAsync(connection, Entry(3, deleted, Now));

        var found = await new Entries(context)
            .ProjectsWithEntriesAsync(TestContext.Current.CancellationToken);

        // Distinct, so that a project with a million rows is one answer, and
        // including the one no project row names any more.
        Assert.Equal(new[] { project.Id, deleted }.Order(), found.Order());
    }

    private static Task SweepAsync(LogaffeDbContext context) =>
        new SweepExpiredEntries(new Projects(context), new Entries(context), new StoppedClock(Now))
            .ExecuteAsync(TestContext.Current.CancellationToken);

    private static LogEntry Entry(
        long id, Guid projectId, DateTimeOffset receivedAt, DateTimeOffset? happened = null) => new()
    {
        Id = id,
        ProjectId = projectId,
        EventTime = happened ?? receivedAt,
        ReceiptTime = receivedAt,
        Level = Level.Information,
        MessageTemplate = "Handled {Path}",
        RenderedMessage = "Handled /orders",
        MessageTruncated = false,
        ExceptionTruncated = false,
    };

    private static async Task StoreAsync(NpgsqlConnection connection, LogEntry entry)
    {
        await using var command = new NpgsqlCommand(
            """
            insert into log_entry (
                id, project_id, event_time, receipt_time, level,
                message_template, rendered_message, message_truncated, exception_truncated)
            values (
                @id, @project_id, @event_time, @receipt_time, @level,
                @message_template, @rendered_message, false, false)
            """,
            connection);

        command.Parameters.AddWithValue("id", entry.Id);
        command.Parameters.AddWithValue("project_id", entry.ProjectId);
        command.Parameters.AddWithValue("event_time", entry.EventTime);
        command.Parameters.AddWithValue("receipt_time", entry.ReceiptTime);
        command.Parameters.AddWithValue("level", (short)entry.Level);
        command.Parameters.AddWithValue("message_template", entry.MessageTemplate);
        command.Parameters.AddWithValue("rendered_message", entry.RenderedMessage);

        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<List<long>> IdsAsync(NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand(
            "select id from log_entry order by id", connection);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);

        var ids = new List<long>();
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            ids.Add(reader.GetInt64(0));
        }

        return ids;
    }

    private static async Task<NpgsqlConnection> OpenAsync(string connectionString)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        return connection;
    }

    private static async Task<LogaffeDbContext> MigratedAsync(string connectionString)
    {
        var context = new LogaffeDbContext(
            new DbContextOptionsBuilder<LogaffeDbContext>().UseNpgsql(connectionString).Options);
        await new SchemaMigrator(context, NullLogger<SchemaMigrator>.Instance)
            .ApplyAsync(TestContext.Current.CancellationToken);

        return context;
    }

    private sealed class StoppedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
