using Logaffe.Application.Operations;
using Logaffe.Application.Ports;
using Logaffe.Domain.Projects;
using Logaffe.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Logaffe.IntegrationTests;

/// <summary>
/// The tally table against a real Postgres.
/// </summary>
/// <remarks>
/// What is asked here is the half no substitute can vouch for: that a flush
/// meeting an hour it already wrote adds to it rather than failing or replacing
/// it, that the key really does hold one row per project per hour, and that the
/// two deletes take what they claim. What the acts above decide — which hour a
/// batch lands in, and what a failed write does with its counts — is asked of
/// the acts, in the unit tests.
/// </remarks>
[Collection(nameof(PostgresCollection))]
public sealed class TallyStoreTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Now = new(2026, 8, 21, 14, 30, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset Hour = Tallying.HourOf(Now);

    [Fact]
    public async Task A_flush_writes_the_hours_it_carries()
    {
        await using var context = await MigratedAsync();
        var api = await ProjectAsync(context, "api");
        var web = await ProjectAsync(context, "web");
        var tallies = new Tallies(context);

        await tallies.AddAsync(
            [Increment(api, Hour, 40, 2), Increment(web, Hour, 7, 0)],
            TestContext.Current.CancellationToken);

        var rows = await ReadAsync(context, api);
        Assert.Equal(40, Assert.Single(rows).Entries);
        Assert.Equal(2, rows[0].AtErrorOrAbove);
    }

    [Fact]
    public async Task A_second_flush_into_one_hour_adds_to_it()
    {
        await using var context = await MigratedAsync();
        var api = await ProjectAsync(context, "api");
        var tallies = new Tallies(context);

        await tallies.AddAsync([Increment(api, Hour, 40, 2)], TestContext.Current.CancellationToken);
        await tallies.AddAsync([Increment(api, Hour, 5, 1)], TestContext.Current.CancellationToken);

        // A flush carries what arrived since the last one, so an hour that took
        // the second increment as its total would hold its final minute.
        var row = Assert.Single(await ReadAsync(context, api));
        Assert.Equal(45, row.Entries);
        Assert.Equal(3, row.AtErrorOrAbove);
    }

    [Fact]
    public async Task Sixty_flushes_of_one_hour_are_one_row()
    {
        await using var context = await MigratedAsync();
        var api = await ProjectAsync(context, "api");
        var tallies = new Tallies(context);

        // A minute at a time, which is what an hour of an installation actually
        // looks like.
        for (var minute = 0; minute < 60; minute++)
        {
            await tallies.AddAsync(
                [Increment(api, Hour, 10, 1)], TestContext.Current.CancellationToken);
        }

        var row = Assert.Single(await ReadAsync(context, api));
        Assert.Equal(600, row.Entries);
        Assert.Equal(60, row.AtErrorOrAbove);
    }

    [Fact]
    public async Task One_flush_straddling_an_hour_writes_two_rows()
    {
        await using var context = await MigratedAsync();
        var api = await ProjectAsync(context, "api");
        var tallies = new Tallies(context);

        await tallies.AddAsync(
            [Increment(api, Hour, 4, 0), Increment(api, Hour.AddHours(1), 6, 0)],
            TestContext.Current.CancellationToken);

        var rows = await ReadAsync(context, api);
        Assert.Equal([Hour, Hour.AddHours(1)], rows.Select(row => row.Hour));
        Assert.Equal([4L, 6L], rows.Select(row => row.Entries));
    }

    [Fact]
    public async Task An_hour_is_read_back_at_UTC()
    {
        await using var context = await MigratedAsync();
        var api = await ProjectAsync(context, "api");
        var tallies = new Tallies(context);

        await tallies.AddAsync([Increment(api, Hour, 1, 0)], TestContext.Current.CancellationToken);
        context.ChangeTracker.Clear();

        // The condition of ADR 0050 measures an hour against the same hour of
        // other days, so what comes off the column has to be the hour that went
        // in and not the same instant somewhere else.
        var row = Assert.Single(await ReadAsync(context, api));
        Assert.Equal(TimeSpan.Zero, row.Hour.Offset);
        Assert.Equal(14, row.Hour.Hour);
    }

    [Fact]
    public async Task A_read_takes_the_range_it_was_given_and_nothing_either_side()
    {
        await using var context = await MigratedAsync();
        var api = await ProjectAsync(context, "api");
        var tallies = new Tallies(context);

        await tallies.AddAsync(
            [
                Increment(api, Hour.AddHours(-1), 1, 0),
                Increment(api, Hour, 2, 0),
                Increment(api, Hour.AddHours(1), 3, 0),
                Increment(api, Hour.AddHours(2), 4, 0),
            ],
            TestContext.Current.CancellationToken);

        var rows = await tallies.ReadAsync(
            api, Hour, Hour.AddHours(2), TestContext.Current.CancellationToken);

        // From is included and to is not, which is what makes two ranges meeting
        // at an hour not both hold it.
        Assert.Equal([2L, 3L], rows.Select(row => row.Entries));
    }

    [Fact]
    public async Task A_read_of_one_project_leaves_another_alone()
    {
        await using var context = await MigratedAsync();
        var api = await ProjectAsync(context, "api");
        var web = await ProjectAsync(context, "web");
        var tallies = new Tallies(context);

        await tallies.AddAsync(
            [Increment(api, Hour, 2, 0), Increment(web, Hour, 99, 9)],
            TestContext.Current.CancellationToken);

        Assert.Equal(2, Assert.Single(await ReadAsync(context, api)).Entries);
    }

    [Fact]
    public async Task An_hour_outside_the_period_goes_and_one_inside_it_stays()
    {
        await using var context = await MigratedAsync();
        var api = await ProjectAsync(context, "api");
        var tallies = new Tallies(context);

        await tallies.AddAsync(
            [
                Increment(api, Hour.AddDays(-Tallying.RetentionDays + 1), 1, 0),
                Increment(api, Hour.AddDays(-Tallying.RetentionDays - 1), 2, 0),
            ],
            TestContext.Current.CancellationToken);

        await SweepAsync(context);

        Assert.Equal([1L], (await ReadAsync(context, api)).Select(row => row.Entries));
    }

    [Fact]
    public async Task A_short_window_on_the_project_does_not_shorten_its_history()
    {
        await using var context = await MigratedAsync();
        // A day of entries, and a year of the history behind them. The project
        // with the shortest window is the one most likely to be busy, and it is
        // the one that would otherwise never have a baseline (ADR 0047).
        var api = await ProjectAsync(context, "api", RetentionWindow.OfDays(1));
        var tallies = new Tallies(context);

        await tallies.AddAsync(
            [Increment(api, Hour.AddDays(-200), 5, 0)], TestContext.Current.CancellationToken);

        await SweepAsync(context);

        Assert.Single(await ReadAsync(context, api));
    }

    [Fact]
    public async Task The_hours_a_deleted_project_left_are_taken()
    {
        await using var context = await MigratedAsync();
        var api = await ProjectAsync(context, "api");
        var tallies = new Tallies(context);

        await tallies.AddAsync([Increment(api, Hour, 5, 0)], TestContext.Current.CancellationToken);

        // Deleting has to succeed with a tally in the table, which is the whole
        // reason there is no foreign key: a project goes at once and what
        // counted it follows in the background (ADR 0019).
        var project = await context.Projects.SingleAsync(
            p => p.Id == api, TestContext.Current.CancellationToken);
        context.Projects.Remove(project);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        await SweepAsync(context);

        Assert.Empty(await ReadAsync(context, api));
    }

    [Fact]
    public async Task The_oldest_hour_is_how_much_history_there_is()
    {
        await using var context = await MigratedAsync();
        var api = await ProjectAsync(context, "api");
        var tallies = new Tallies(context);

        await tallies.AddAsync(
            [
                Increment(api, Hour.AddDays(-20), 5, 0),
                Increment(api, Hour.AddDays(-1), 5, 0),
                Increment(api, Hour, 5, 0),
            ],
            TestContext.Current.CancellationToken);

        // What decides whether a rate may be extrapolated at all: the fortnight
        // is measured from the first row there is, not from the ones inside it.
        Assert.Equal(
            Hour.AddDays(-20),
            await tallies.OldestHourAsync(api, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_project_with_no_hours_has_no_oldest_one()
    {
        await using var context = await MigratedAsync();
        var api = await ProjectAsync(context, "api");
        var web = await ProjectAsync(context, "web");
        var tallies = new Tallies(context);

        await tallies.AddAsync(
            [Increment(web, Hour, 5, 0)], TestContext.Current.CancellationToken);

        // A project that has never received anything, and a project created five
        // minutes ago, look the same here and are answered the same way: there
        // is nothing to say about their rate.
        Assert.Null(await tallies.OldestHourAsync(api, TestContext.Current.CancellationToken));
    }

    private static TallyIncrement Increment(
        Guid projectId, DateTimeOffset hour, long entries, long atErrorOrAbove) =>
        new()
        {
            ProjectId = projectId,
            Hour = hour,
            Entries = entries,
            AtErrorOrAbove = atErrorOrAbove,
        };

    private static Task<IReadOnlyList<Tally>> ReadAsync(LogaffeDbContext context, Guid projectId) =>
        new Tallies(context).ReadAsync(
            projectId,
            DateTimeOffset.MinValue,
            DateTimeOffset.MaxValue,
            TestContext.Current.CancellationToken);

    private static Task SweepAsync(LogaffeDbContext context) =>
        new SweepExpiredTallies(new Projects(context), new Tallies(context), new StoppedClock(Now))
            .ExecuteAsync(TestContext.Current.CancellationToken);

    private static async Task<Guid> ProjectAsync(
        LogaffeDbContext context, string name, RetentionWindow? retention = null)
    {
        var project = Project.Create(name, retention ?? RetentionWindow.OfDays(30), Now.AddDays(-1));
        context.Projects.Add(project);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        return project.Id;
    }

    private async Task<LogaffeDbContext> MigratedAsync()
    {
        var context = new LogaffeDbContext(
            new DbContextOptionsBuilder<LogaffeDbContext>()
                .UseNpgsql(await postgres.CreateDatabaseAsync())
                .Options);

        await new SchemaMigrator(context, NullLogger<SchemaMigrator>.Instance)
            .ApplyAsync(TestContext.Current.CancellationToken);

        return context;
    }

    private sealed class StoppedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
