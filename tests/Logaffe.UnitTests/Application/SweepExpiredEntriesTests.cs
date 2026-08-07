using Logaffe.Application.Operations;
using Logaffe.Domain.Projects;

namespace Logaffe.UnitTests.Application;

/// <summary>
/// The sweep decides two things: which cutoff each project gets, and when it has
/// asked enough times. What the delete costs and which rows it touches is the
/// store's, and is asked of a real database.
/// </summary>
public sealed class SweepExpiredEntriesTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);

    private readonly InMemoryProjects _projects = new();
    private readonly RecordingEntries _entries = new();
    private readonly StoppedClock _clock = new(Now);

    [Fact]
    public async Task Each_project_is_swept_at_its_own_window()
    {
        var short_ = _projects.Holding("api", RetentionWindow.OfDays(7), Now);
        var long_ = _projects.Holding("web", RetentionWindow.OfDays(90), Now);

        await Sweep().ExecuteAsync(TestContext.Current.CancellationToken);

        // Retention is per project, which is the whole reason the entries are
        // deleted as rows rather than as dropped partitions (ADR 0023).
        Assert.Equal(Now.AddDays(-7), _entries.CutoffFor(short_.Id));
        Assert.Equal(Now.AddDays(-90), _entries.CutoffFor(long_.Id));
    }

    [Fact]
    public async Task The_cutoff_moves_with_the_clock()
    {
        var project = _projects.Holding("api", RetentionWindow.OfDays(30), Now);
        _clock.Now = Now.AddHours(6);

        await Sweep().ExecuteAsync(TestContext.Current.CancellationToken);

        Assert.Equal(Now.AddHours(6).AddDays(-30), _entries.CutoffFor(project.Id));
    }

    [Fact]
    public async Task A_portion_that_comes_back_full_is_asked_again()
    {
        var project = _projects.Holding("api", RetentionWindow.OfDays(7), Now);
        // Two full portions and a short one: the store has more to give until it
        // does not.
        _entries.Removing(SweepExpiredEntries.Portion, SweepExpiredEntries.Portion, 12);

        await Sweep().ExecuteAsync(TestContext.Current.CancellationToken);

        Assert.Equal(3, _entries.PortionsFor(project.Id));
    }

    [Fact]
    public async Task A_portion_that_comes_back_short_is_the_last_one()
    {
        var project = _projects.Holding("api", RetentionWindow.OfDays(7), Now);
        _entries.Removing(0);

        await Sweep().ExecuteAsync(TestContext.Current.CancellationToken);

        // Nothing outside the window is the ordinary case, hour after hour, and
        // it has to cost one statement.
        Assert.Equal(1, _entries.PortionsFor(project.Id));
    }

    [Fact]
    public async Task The_entries_of_a_project_that_is_gone_are_taken_whole()
    {
        var live = _projects.Holding("api", RetentionWindow.OfDays(7), Now);
        var deleted = Guid.CreateVersion7();
        _entries.Holding(live.Id, deleted);

        await Sweep().ExecuteAsync(TestContext.Current.CancellationToken);

        // A project goes at once and its entries follow in the background
        // (ADR 0019). There is no window left to read, so there is no cutoff to
        // apply — everything goes, which is the same thing said with a window of
        // nothing.
        Assert.Equal(DateTimeOffset.MaxValue, _entries.CutoffFor(deleted));
    }

    [Fact]
    public async Task A_project_that_still_exists_is_swept_only_at_its_window()
    {
        var project = _projects.Holding("api", RetentionWindow.OfDays(7), Now);
        _entries.Holding(project.Id);

        await Sweep().ExecuteAsync(TestContext.Current.CancellationToken);

        // The table naming a project the installation still has is the ordinary
        // case, and it must not read as abandoned.
        Assert.Equal(1, _entries.PortionsFor(project.Id));
        Assert.Equal(Now.AddDays(-7), _entries.CutoffFor(project.Id));
    }

    [Fact]
    public async Task An_installation_with_no_projects_sweeps_nothing()
    {
        await Sweep().ExecuteAsync(TestContext.Current.CancellationToken);

        Assert.Empty(_entries.Removals);
    }

    private SweepExpiredEntries Sweep() => new(_projects, _entries, _clock);
}
