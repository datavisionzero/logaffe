using Logaffe.Application.Operations;
using Logaffe.Domain.Projects;

namespace Logaffe.UnitTests.Application;

/// <summary>
/// The reading in front of the retention change. What it decides is the cutoff —
/// which has to be the sweep's, or the operator is shown a number that is not
/// the one that goes — and that it asks about a project rather than about an
/// installation.
/// </summary>
public sealed class CountEntriesOutsideWindowTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);

    private readonly InMemoryProjects _projects = new();
    private readonly RecordingEntries _entries = new();
    private readonly StoppedClock _clock = new(Now);

    [Fact]
    public async Task It_counts_from_the_window_that_has_not_been_applied()
    {
        var project = _projects.Holding("api", RetentionWindow.OfDays(90), Now);
        _entries.Counting = 4711;

        var outside = await Count(project.Id, RetentionWindow.OfDays(7));

        Assert.Equal(4711, outside);
        // The proposed seven days, not the ninety the project is still on. Both
        // exist at this moment and only one of them is the question.
        Assert.Equal((project.Id, Now.AddDays(-7)), Assert.Single(_entries.Counts));
    }

    [Fact]
    public async Task It_is_the_cutoff_the_sweep_would_use()
    {
        var project = _projects.Holding("api", RetentionWindow.OfDays(30), Now);

        await Count(project.Id, RetentionWindow.OfDays(30));
        var swept = new RecordingEntries();
        await new SweepExpiredEntries(_projects, swept, _clock)
            .ExecuteAsync(TestContext.Current.CancellationToken);

        // Two acts, one arithmetic. A reading that disagreed with the sweep
        // would be worse than no reading at all: the operator would be shown a
        // number and then lose a different one.
        Assert.Equal(swept.CutoffFor(project.Id), _entries.Counts.Single().ReceivedBefore);
    }

    [Fact]
    public async Task Nothing_outside_the_window_is_nought_and_not_a_warning()
    {
        var project = _projects.Holding("api", RetentionWindow.OfDays(7), Now);
        _entries.Counting = 0;

        // The ordinary case: an operator opening the field and putting it back
        // where it was, and every raise. Nothing about it is exceptional here —
        // what a screen does with a nought is the screen's.
        Assert.Equal(0, await Count(project.Id, RetentionWindow.OfDays(7)));
    }

    [Fact]
    public async Task A_project_that_is_gone_is_not_a_count_of_nothing()
    {
        // Which is what another tab deleting it looks like. Answering zero would
        // read as "this change costs nothing" for a project that cannot be
        // changed at all.
        Assert.Null(await Count(Guid.CreateVersion7(), RetentionWindow.OfDays(7)));
        Assert.Empty(_entries.Counts);
    }

    [Fact]
    public async Task It_asks_about_one_project_and_not_the_installation()
    {
        var project = _projects.Holding("api", RetentionWindow.OfDays(30), Now);
        _projects.Holding("web", RetentionWindow.OfDays(30), Now);

        await Count(project.Id, RetentionWindow.OfDays(7));

        Assert.Equal(project.Id, _entries.Counts.Single().ProjectId);
    }

    [Fact]
    public async Task Reading_it_changes_no_window()
    {
        var project = _projects.Holding("api", RetentionWindow.OfDays(90), Now);

        await Count(project.Id, RetentionWindow.OfDays(7));

        // It is a read in front of the act and not part of it, so it can be
        // asked as often as the operator likes for windows they never apply.
        Assert.Equal(RetentionWindow.OfDays(90), project.Retention);
        Assert.Equal(0, _projects.Writes);
    }

    private Task<long?> Count(Guid id, RetentionWindow proposed) =>
        new CountEntriesOutsideWindow(_projects, _entries, _clock)
            .ExecuteAsync(id, proposed, TestContext.Current.CancellationToken);
}
