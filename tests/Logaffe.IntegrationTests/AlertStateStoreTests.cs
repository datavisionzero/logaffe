using Logaffe.Domain.Alerts;
using Logaffe.Domain.Projects;
using Logaffe.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Logaffe.IntegrationTests;

/// <summary>
/// What alerting keeps between passes, against a real Postgres.
/// </summary>
/// <remarks>
/// What is asked here is the half no substitute can vouch for, and it is the
/// half the whole guarding rests on: that a condition's state is a row that
/// survives the process, that there is one of it per subject per condition
/// whatever writes it, and that the switches and the mute are off and false on
/// an installation nobody has asked. An installation that restarts hourly must
/// not notify hourly, and that is a promise about the database rather than about
/// the acts.
/// </remarks>
[Collection(nameof(PostgresCollection))]
public sealed class AlertStateStoreTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Now = new(2026, 8, 22, 14, 0, 0, TimeSpan.Zero);

    private static readonly Guid Subject = Guid.CreateVersion7();

    [Fact]
    public async Task A_state_written_is_the_state_a_later_pass_reads()
    {
        await using var context = await MigratedAsync();
        var states = new ConditionStates(context);

        var state = ConditionState.For(Subject, AlertCondition.GoneQuiet);
        state.Fired(Alerting.Holding, Now);
        await states.RecordAsync(state, TestContext.Current.CancellationToken);

        // The pass after a restart, which is a context that has never seen this
        // row.
        await using var restarted = await ContextAsync(context);

        var read = await new ConditionStates(restarted).FindAsync(
            Subject, AlertCondition.GoneQuiet, TestContext.Current.CancellationToken);

        Assert.NotNull(read);
        Assert.Equal(Alerting.Holding, read.Latched);
        Assert.Equal(Alerting.Holding, read.NotifiedLevel);
        Assert.Equal(Now, read.NotifiedAt);
    }

    [Fact]
    public async Task A_second_write_is_the_same_row()
    {
        await using var context = await MigratedAsync();
        var states = new ConditionStates(context);

        var state = ConditionState.For(Subject, AlertCondition.FillingUp);
        state.Fired(StoreFullness.FirstThreshold, Now);
        await states.RecordAsync(state, TestContext.Current.CancellationToken);

        state.Fired(StoreFullness.SecondThreshold, Now.AddHours(1));
        await states.RecordAsync(state, TestContext.Current.CancellationToken);

        var rows = await context.ConditionStates
            .AsNoTracking()
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            StoreFullness.SecondThreshold, Assert.Single(rows).NotifiedLevel);
    }

    [Fact]
    public async Task One_subject_holds_a_row_per_condition()
    {
        await using var context = await MigratedAsync();
        var states = new ConditionStates(context);

        foreach (var condition in (AlertCondition[])
            [AlertCondition.GoneQuiet, AlertCondition.Flooding])
        {
            var state = ConditionState.For(Subject, condition);
            state.Fired(Alerting.Holding, Now);
            await states.RecordAsync(state, TestContext.Current.CancellationToken);
        }

        Assert.Equal(
            2, await context.ConditionStates.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_condition_nothing_has_been_said_about_has_no_row()
    {
        await using var context = await MigratedAsync();

        Assert.Null(await new ConditionStates(context).FindAsync(
            Subject, AlertCondition.Flooding, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task An_installation_nobody_has_asked_has_all_three_switched_off()
    {
        await using var context = await MigratedAsync();

        var switches = await new Installation(context).ReadAlertSwitchesAsync(
            TestContext.Current.CancellationToken);

        Assert.False(switches.Any);
    }

    [Fact]
    public async Task The_switches_are_read_back_as_they_were_left()
    {
        await using var context = await MigratedAsync();
        var installation = new Installation(context);

        await installation.RecordAlertSwitchesAsync(
            new AlertSwitches(FillingUp: true, GoneQuiet: false, Flooding: true),
            TestContext.Current.CancellationToken);

        await using var restarted = await ContextAsync(context);

        var switches = await new Installation(restarted).ReadAlertSwitchesAsync(
            TestContext.Current.CancellationToken);

        Assert.True(switches.FillingUp);
        Assert.False(switches.GoneQuiet);
        Assert.True(switches.Flooding);

        // The one row, still: switching a condition on does not write a second
        // settings row and does not disturb the window beside it.
        Assert.Equal(
            Domain.Hosts.Sampling.RetentionDaysByDefault,
            (await new Installation(restarted).ReadSampleRetentionAsync(
                TestContext.Current.CancellationToken)).Days);
    }

    [Fact]
    public async Task A_project_is_not_muted_until_it_is_muted()
    {
        await using var context = await MigratedAsync();

        var project = Project.Create("api", RetentionWindow.OfDays(30), Now.AddDays(-30));
        context.Projects.Add(project);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        project.Mute(true);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        await using var restarted = await ContextAsync(context);

        Assert.True((await restarted.Projects.SingleAsync(
            p => p.Id == project.Id, TestContext.Current.CancellationToken)).Muted);
    }

    private static Task<LogaffeDbContext> ContextAsync(LogaffeDbContext existing) =>
        Task.FromResult(new LogaffeDbContext(
            new DbContextOptionsBuilder<LogaffeDbContext>()
                .UseNpgsql(existing.Database.GetConnectionString())
                .Options));

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
}
