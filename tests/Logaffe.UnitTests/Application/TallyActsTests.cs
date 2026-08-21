using Logaffe.Application.Operations;
using Logaffe.Domain.Projects;

namespace Logaffe.UnitTests.Application;

/// <summary>
/// The counter the delivery path moves, and the flush that writes it down.
/// </summary>
/// <remarks>
/// What is asked here is what the two decide: which hour a batch lands in, what
/// a take leaves behind, and what a failed write does with the counts it was
/// holding. Whether the rows come out of a real database with the right numbers
/// on them is asked of a real Postgres.
/// </remarks>
public sealed class RunningTallyTests
{
    private static readonly Guid Project = Guid.CreateVersion7();

    private static readonly DateTimeOffset Hour = new(2026, 8, 21, 14, 0, 0, TimeSpan.Zero);

    private readonly RunningTally _running = new();

    [Fact]
    public void A_counter_nothing_has_reached_has_nothing_to_take()
    {
        Assert.Empty(_running.Take());
    }

    [Fact]
    public void Batches_in_one_hour_come_out_as_one_increment()
    {
        _running.Record(Project, Hour.AddMinutes(3), 10, 1);
        _running.Record(Project, Hour.AddMinutes(41), 5, 0);

        var increment = Assert.Single(_running.Take());

        Assert.Equal(Project, increment.ProjectId);
        Assert.Equal(Hour, increment.Hour);
        Assert.Equal(15, increment.Entries);
        Assert.Equal(1, increment.AtErrorOrAbove);
    }

    [Fact]
    public void An_hour_boundary_inside_one_flush_is_two_increments()
    {
        // A minute's flush can straddle two hours, and the row it belongs to is
        // decided by the receipt of the batch rather than by when the flush ran.
        _running.Record(Project, Hour.AddMinutes(59), 4, 0);
        _running.Record(Project, Hour.AddMinutes(61), 6, 0);

        var increments = _running.Take().OrderBy(increment => increment.Hour).ToList();

        Assert.Equal([Hour, Hour.AddHours(1)], increments.Select(increment => increment.Hour));
        Assert.Equal([4L, 6L], increments.Select(increment => increment.Entries));
    }

    [Fact]
    public void Two_projects_are_two_increments()
    {
        var other = Guid.CreateVersion7();

        _running.Record(Project, Hour, 3, 0);
        _running.Record(other, Hour, 7, 2);

        var increments = _running.Take();

        Assert.Equal(2, increments.Count);
        Assert.Equal(3, increments.Single(i => i.ProjectId == Project).Entries);
        Assert.Equal(7, increments.Single(i => i.ProjectId == other).Entries);
    }

    [Fact]
    public void Taking_starts_again_from_nothing()
    {
        _running.Record(Project, Hour, 10, 0);
        _running.Take();

        // Not "the same numbers a second time": the flush that took them has
        // written them, and an increment handed over twice is an hour counted
        // twice.
        Assert.Empty(_running.Take());
    }

    [Fact]
    public void A_batch_of_nothing_makes_no_increment()
    {
        _running.Record(Project, Hour, 0, 0);

        Assert.Empty(_running.Take());
    }

    [Fact]
    public void Put_back_counts_are_taken_by_the_next_flush()
    {
        _running.Record(Project, Hour, 10, 1);
        var taken = _running.Take();

        _running.PutBack(taken);
        _running.Record(Project, Hour, 5, 0);

        var increment = Assert.Single(_running.Take());

        // Added to what arrived in the meantime rather than replacing it: the
        // minute that failed to write and the minute after it are the same hour.
        Assert.Equal(15, increment.Entries);
        Assert.Equal(1, increment.AtErrorOrAbove);
    }

    [Fact]
    public void Deliveries_arriving_together_are_all_counted()
    {
        // The path this is on is the hottest in the product and is not
        // serialised anywhere, so the adds have to survive being concurrent.
        Parallel.For(0, 500, _ => _running.Record(Project, Hour, 2, 1));

        var increment = Assert.Single(_running.Take());

        Assert.Equal(1_000, increment.Entries);
        Assert.Equal(500, increment.AtErrorOrAbove);
    }
}

/// <summary>
/// The flush: one small write a minute, and what it does when there is nothing
/// to write or the write fails.
/// </summary>
public sealed class FlushTheTallyTests
{
    private static readonly Guid Project = Guid.CreateVersion7();

    private static readonly DateTimeOffset Hour = new(2026, 8, 21, 14, 0, 0, TimeSpan.Zero);

    private readonly RunningTally _running = new();
    private readonly RecordingTallies _tallies = new();

    [Fact]
    public async Task A_pass_with_nothing_counted_does_not_touch_the_store()
    {
        await Flush();

        // The ordinary state of an installation whose projects are quiet, once a
        // minute for as long as it is up.
        Assert.Empty(_tallies.Flushes);
    }

    [Fact]
    public async Task What_was_counted_is_written_and_the_counter_starts_again()
    {
        _running.Record(Project, Hour.AddMinutes(10), 12, 2);

        await Flush();
        await Flush();

        var flush = Assert.Single(_tallies.Flushes);
        Assert.Equal(12, Assert.Single(flush).Entries);
    }

    [Fact]
    public async Task Two_flushes_of_one_hour_add_up()
    {
        _running.Record(Project, Hour.AddMinutes(1), 10, 1);
        await Flush();

        _running.Record(Project, Hour.AddMinutes(2), 5, 0);
        await Flush();

        var row = _tallies.Row(Project, Hour);
        Assert.Equal(15, row.Entries);
        Assert.Equal(1, row.AtErrorOrAbove);
    }

    [Fact]
    public async Task A_write_that_failed_leaves_the_counts_for_the_next_pass()
    {
        _running.Record(Project, Hour, 10, 1);
        _tallies.Refusing = new InvalidOperationException("the store cannot be reached");

        await Assert.ThrowsAsync<InvalidOperationException>(Flush);

        // The write is one transaction, so what threw stored none of it and
        // these are owed to the next minute. Losing them here would make a
        // database that blinked cost what a restart costs.
        _tallies.Refusing = null;
        await Flush();

        Assert.Equal(10, _tallies.Row(Project, Hour).Entries);
    }

    [Fact]
    public async Task The_installation_stopping_does_not_put_anything_back()
    {
        _running.Record(Project, Hour, 10, 1);
        _tallies.Refusing = new OperationCanceledException();

        await Assert.ThrowsAsync<OperationCanceledException>(Flush);

        // There is no next pass to hand them to, and the memory holding them is
        // going away with the process.
        Assert.Empty(_running.Take());
    }

    private Task Flush() =>
        new FlushTheTally(_running, _tallies).ExecuteAsync(TestContext.Current.CancellationToken);
}

/// <summary>
/// The sweep: one cutoff for every project, and the hours a deleted project left.
/// </summary>
public sealed class SweepExpiredTalliesTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 21, 14, 30, 0, TimeSpan.Zero);

    private readonly InMemoryProjects _projects = new();
    private readonly RecordingTallies _tallies = new();
    private readonly StoppedClock _clock = new(Now);

    [Fact]
    public async Task The_cutoff_is_the_tallys_own_period_and_not_any_projects_window()
    {
        // A week-long window on the project, and the history behind it stays for
        // as long as every other project's: without that, the projects with the
        // shortest windows — which are the busy ones — could never have a
        // baseline at all.
        _projects.Holding("api", RetentionWindow.OfDays(7), Now);

        await Sweep();

        Assert.Equal(
            Tallying.HourOf(Now).AddDays(-Tallying.RetentionDays),
            Assert.Single(_tallies.Cutoffs));
    }

    [Fact]
    public async Task One_cutoff_serves_every_project()
    {
        _projects.Holding("api", RetentionWindow.OfDays(7), Now);
        _projects.Holding("web", RetentionWindow.OfDays(90), Now);

        await Sweep();

        // One statement across the table, where the entry sweep walks: the
        // period is the same for every project, and the table is small enough
        // that a walk would be machinery for nothing.
        Assert.Single(_tallies.Cutoffs);
    }

    [Fact]
    public async Task The_hours_a_deleted_project_left_are_taken_whole()
    {
        var live = _projects.Holding("api", RetentionWindow.OfDays(30), Now);
        var gone = Guid.CreateVersion7();
        _tallies.Holding.AddRange([live.Id, gone]);

        await Sweep();

        // There is no foreign key from a tally to its project, for the reason
        // ADR 0019 gives for the entries not having one — so nothing but this
        // can reach them.
        Assert.Equal([gone], _tallies.Removed);
    }

    [Fact]
    public async Task A_project_that_still_exists_is_never_taken_whole()
    {
        var live = _projects.Holding("api", RetentionWindow.OfDays(30), Now);
        _tallies.Holding.Add(live.Id);

        await Sweep();

        Assert.Empty(_tallies.Removed);
    }

    private Task Sweep() =>
        new SweepExpiredTallies(_projects, _tallies, _clock)
            .ExecuteAsync(TestContext.Current.CancellationToken);
}
