using Logaffe.Application.Operations;
using Logaffe.Application.Ports;
using Logaffe.Domain.Entries;
using Logaffe.Domain.Projects;
using Logaffe.Domain.Queries;

namespace Logaffe.UnitTests.Application;

/// <summary>
/// The three reads of <c>docs/querying.md</c>: the page, the count, and one
/// entry.
/// </summary>
public sealed class QueryActsTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 8, 10, 0, 0, TimeSpan.Zero);

    private readonly InMemoryProjects _projects = new();
    private readonly RecordingReader _reader = new();

    private Project Holding() =>
        _projects.Holding("orders", RetentionWindow.OfDays(30), Now);

    [Fact]
    public async Task A_page_that_filled_carries_the_cursor_of_its_last_entry()
    {
        var project = Holding();
        _reader.PagingOf(Page.Size);

        var read = await Search(project.Id);

        Assert.False(read!.Expired);
        Assert.Equal(Page.Size, read.Answer!.Entries.Count);

        // The last entry of the page and not the first: the order is newest
        // first, so where the next page resumes is the oldest one on this.
        var last = read.Answer.Entries[^1];
        Assert.Equal(new EntryCursor(last.EventTime, last.Id), read.Answer.Next);
    }

    [Fact]
    public async Task A_page_that_did_not_fill_is_the_last_one()
    {
        var project = Holding();
        _reader.PagingOf(Page.Size - 1);

        var read = await Search(project.Id);

        // Read off the length rather than bought with an extra row on every
        // page. The cost of being wrong is one empty page at the end of a
        // project that holds a multiple of the page size.
        Assert.Null(read!.Answer!.Next);
    }

    [Fact]
    public async Task An_empty_page_is_not_a_cursor_to_nowhere()
    {
        var project = Holding();

        var read = await Search(project.Id);

        Assert.Empty(read!.Answer!.Entries);
        Assert.Null(read.Answer.Next);
    }

    [Fact]
    public async Task A_page_resumes_after_the_cursor_it_was_given()
    {
        var project = Holding();
        var after = new EntryCursor(Now, 4711);

        await Search(project.Id, after: after);

        Assert.Equal(after, _reader.Pages.Single().After);
    }

    [Fact]
    public async Task Every_read_runs_inside_the_project_it_was_asked_for()
    {
        var project = Holding();

        await Search(project.Id);
        await Count(project.Id);
        await new ReadEntry(_projects, _reader).ExecuteAsync(
            project.Id, 12, TestContext.Current.CancellationToken);

        Assert.Equal(project.Id, _reader.Pages.Single().ProjectId);
        Assert.Equal(project.Id, _reader.Counts.Single().ProjectId);
        Assert.Equal((project.Id, 12L), _reader.Lookups.Single());
    }

    [Fact]
    public async Task There_is_no_reading_a_project_that_does_not_exist()
    {
        // A project deleted in another tab, and the window in which its entries
        // are still in the table while the sweep takes them (ADR 0019). They are
        // unreachable because every read runs inside a project and this one is
        // gone — so nothing is asked of the entry table at all.
        var gone = Guid.CreateVersion7();

        Assert.Null(await Search(gone));
        Assert.Null(await Count(gone));
        Assert.Null(await new ReadEntry(_projects, _reader).ExecuteAsync(
            gone, 12, TestContext.Current.CancellationToken));

        Assert.Empty(_reader.Pages);
        Assert.Empty(_reader.Counts);
        Assert.Empty(_reader.Lookups);
    }

    [Fact]
    public async Task A_range_that_ends_where_it_starts_is_refused_rather_than_run()
    {
        var project = Holding();
        var filters = new EntryFilters { From = Now, Until = Now };

        // A malformed question, not an empty answer. Answering "no entries"
        // would send the operator looking for a delivery problem.
        await Assert.ThrowsAsync<ArgumentException>(() => Search(project.Id, filters));
        await Assert.ThrowsAsync<ArgumentException>(() => Count(project.Id, filters));

        Assert.Empty(_reader.Pages);
        Assert.Empty(_reader.Counts);
    }

    [Fact]
    public async Task An_expired_read_says_what_to_narrow_rather_than_failing()
    {
        var project = Holding();
        _reader.Expiring = true;

        var page = await Search(project.Id);
        var count = await Count(project.Id);

        // Never an exception out of this layer: the operator adjusts a filter
        // and the agent is handed the same fact as data (ADR 0026, ADR 0012).
        Assert.True(page!.Expired);
        Assert.Equal([Narrowing.TimeRange], page.Narrow);

        Assert.True(count!.Expired);
        Assert.Equal([Narrowing.TimeRange], count.Narrow);
    }

    [Fact]
    public async Task An_expired_read_is_told_about_the_filters_it_actually_ran_with()
    {
        var project = Holding();
        _reader.Expiring = true;

        var filters = new EntryFilters
        {
            From = Now.AddHours(-1),
            Until = Now,
            ExceptionText = SearchText.Create("nullreference"),
        };

        var read = await Count(project.Id, filters);

        // The range is already set, so what is left to take off is the filter no
        // index serves (ADR 0028).
        Assert.Equal([Narrowing.ExceptionText], read!.Narrow);
    }

    [Fact]
    public async Task A_count_carries_the_grouping_it_was_asked_for()
    {
        var project = Holding();
        _reader.Counting = [new CountedGroup("Orders.Api", 12)];

        var read = await Count(project.Id, grouping: Grouping.LoggerName);

        Assert.Equal(Grouping.LoggerName, _reader.Counts.Single().Grouping);
        Assert.Equal(new CountedGroup("Orders.Api", 12), Assert.Single(read!.Answer!));
    }

    [Fact]
    public async Task An_ungrouped_count_is_one_group_and_not_a_shape_of_its_own()
    {
        var project = Holding();
        _reader.Counting = [new CountedGroup(null, 40_000)];

        var read = await Count(project.Id);

        // So that a caller reads every count the same way, and the screen showing
        // a grouped one is showing this with one row.
        var group = Assert.Single(read!.Answer!);
        Assert.Null(group.Value);
        Assert.Equal(40_000, group.Entries);
    }

    [Fact]
    public async Task One_entry_comes_back_as_it_is_stored()
    {
        var project = Holding();
        _reader.Finding = An.Entry(12);

        var entry = await new ReadEntry(_projects, _reader).ExecuteAsync(
            project.Id, 12, TestContext.Current.CancellationToken);

        // Whole. The follow-up after a compact search is the exception and the
        // properties, so there is no second shape of an entry here — what shapes
        // one for a consumer is an adapter's job (ADR 0012).
        Assert.Same(_reader.Finding, entry);
    }

    [Fact]
    public async Task An_entry_the_project_does_not_hold_is_absent_rather_than_an_error()
    {
        var project = Holding();

        // An entry that aged out between the page and the click looks like this,
        // and so does an identity somebody guessed.
        Assert.Null(await new ReadEntry(_projects, _reader).ExecuteAsync(
            project.Id, 4711, TestContext.Current.CancellationToken));
    }

    private Task<Read<EntryPage>?> Search(
        Guid projectId, EntryFilters? filters = null, EntryCursor? after = null) =>
        new SearchEntries(_projects, _reader).ExecuteAsync(
            projectId,
            filters ?? EntryFilters.None,
            after,
            TestContext.Current.CancellationToken);

    private Task<Read<IReadOnlyList<CountedGroup>>?> Count(
        Guid projectId, EntryFilters? filters = null, Grouping grouping = Grouping.None) =>
        new CountEntries(_projects, _reader).ExecuteAsync(
            projectId,
            filters ?? EntryFilters.None,
            grouping,
            TimeBucket.Hour,
            TestContext.Current.CancellationToken);
}
