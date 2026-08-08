using Logaffe.Application.Operations;
using Logaffe.Domain.Projects;
using Logaffe.Domain.Queries;

namespace Logaffe.UnitTests.Application;

/// <summary>
/// The live tail of <c>docs/querying.md</c>: what has arrived since the last
/// poll.
/// </summary>
/// <remarks>
/// What this act decides is where a poll resumes from, which is the one thing
/// that cannot be got wrong without losing an entry an operator was watching
/// for. Whether the statement meets the receipt index, and whether the two
/// orders in it do what they claim, is asked of a real Postgres.
/// </remarks>
public sealed class TailActsTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 8, 10, 0, 0, TimeSpan.Zero);

    private readonly InMemoryProjects _projects = new();
    private readonly RecordingReader _reader = new();

    private Project Holding() =>
        _projects.Holding("orders", RetentionWindow.OfDays(30), Now);

    [Fact]
    public async Task The_first_poll_arms_the_tail_rather_than_answering_entries()
    {
        var project = Holding();
        _reader.Newest = new TailCursor(Now, 4711);

        var read = await Tail(project.Id);

        // The view has just loaded its page; what it needs is the position to
        // watch from, and answering it the newest entries would hand back what
        // it is already showing.
        Assert.Empty(read!.Answer!.Entries);
        Assert.Equal(new TailCursor(Now, 4711), read.Answer.Next);
        Assert.False(read.Answer.More);

        // And nothing is read of the entries themselves.
        Assert.Equal(project.Id, Assert.Single(_reader.Armings));
        Assert.Empty(_reader.Polls);
    }

    [Fact]
    public async Task A_tail_on_a_project_holding_nothing_starts_before_everything()
    {
        var project = Holding();
        _reader.Newest = null;

        var read = await Tail(project.Id);

        // Everything delivered from here on arrived while the view was watching,
        // which is exactly what a position before every entry answers — and it
        // is a cursor like any other, so the poll after it is the same call.
        Assert.Equal(TailCursor.Beginning, read!.Answer!.Next);
    }

    [Fact]
    public async Task A_poll_resumes_after_the_cursor_it_was_given()
    {
        var project = Holding();
        var since = new TailCursor(Now, 4711);

        await Tail(project.Id, since: since);

        Assert.Equal(since, _reader.Polls.Single().Since);
        Assert.Empty(_reader.Armings);
    }

    [Fact]
    public async Task A_poll_that_answered_nothing_hands_back_the_cursor_it_was_given()
    {
        var project = Holding();
        var since = new TailCursor(Now, 4711);

        var read = await Tail(project.Id, since: since);

        // The ordinary answer of a quiet project, and it still carries the
        // position of the next poll: following the logs is a loop over the last
        // answer rather than a state the caller keeps.
        Assert.Empty(read!.Answer!.Entries);
        Assert.Equal(since, read.Answer.Next);
        Assert.False(read.Answer.More);
    }

    [Fact]
    public async Task A_poll_resumes_after_the_latest_arrival_and_not_the_last_entry()
    {
        var project = Holding();

        // A sender that was disconnected, which is the case the tail exists for:
        // the entry that arrived last is the oldest of the three by event time,
        // so it is at the bottom of the answer rather than at the top.
        _reader.Arriving =
        [
            An.Entry(1, at: Now, received: Now),
            An.Entry(2, at: Now.AddMinutes(-1), received: Now.AddSeconds(1)),
            An.Entry(3, at: Now.AddMinutes(-10), received: Now.AddSeconds(2)),
        ];

        var read = await Tail(project.Id, since: new TailCursor(Now.AddMinutes(-1), 0));

        // Reading it off the end of the list would hand out a position ahead of
        // entries the next poll has not seen, which is the one way this loses an
        // entry for good.
        Assert.Equal(new TailCursor(Now.AddSeconds(2), 3), read!.Answer!.Next);
    }

    [Fact]
    public async Task Entries_that_arrived_in_one_act_are_stepped_through_by_identity()
    {
        var project = Holding();

        // One batch, one receipt time, which is what the identity in the cursor
        // is for: a position on the timestamp alone would repeat the batch on
        // the next poll or skip the rest of it.
        _reader.Arriving =
        [
            An.Entry(7, at: Now, received: Now),
            An.Entry(9, at: Now.AddSeconds(-1), received: Now),
        ];

        var read = await Tail(project.Id, since: new TailCursor(Now.AddMinutes(-1), 0));

        Assert.Equal(new TailCursor(Now, 9), read!.Answer!.Next);
    }

    [Fact]
    public async Task A_poll_that_filled_says_so_rather_than_losing_the_middle()
    {
        var project = Holding();
        _reader.ArrivingOf(Page.Size);

        var read = await Tail(project.Id, since: new TailCursor(Now.AddMinutes(-1), 0));

        // An interval that cannot keep up is something the caller is told: the
        // cursor names a position in the arrival order, so what is waiting is
        // still ahead of it, and the answer is to poll again rather than to wait
        // the interval out.
        Assert.True(read!.Answer!.More);

        _reader.ArrivingOf(Page.Size - 1);

        Assert.False((await Tail(project.Id, since: new TailCursor(Now, 0)))!.Answer!.More);
    }

    [Fact]
    public async Task A_tail_narrows_with_the_filters_it_was_given_and_no_others()
    {
        var project = Holding();
        var filters = new EntryFilters { From = Now.AddMinutes(-15), MinimumLevel = null };

        await Tail(project.Id, filters, new TailCursor(Now, 0));

        // A filter set that is being watched, not a mode with rules of its own.
        Assert.Equal(filters, _reader.Polls.Single().Filters);
    }

    [Fact]
    public async Task There_is_no_tailing_a_project_that_does_not_exist()
    {
        // A project deleted in another tab, which the view tailing it meets on
        // its next poll.
        var gone = Guid.CreateVersion7();

        Assert.Null(await Tail(gone));

        Assert.Empty(_reader.Polls);
        Assert.Empty(_reader.Armings);
    }

    [Fact]
    public async Task A_range_that_ends_where_it_starts_is_refused_rather_than_run()
    {
        var project = Holding();
        var filters = new EntryFilters { From = Now, Until = Now };

        await Assert.ThrowsAsync<ArgumentException>(() => Tail(project.Id, filters));

        Assert.Empty(_reader.Polls);
        Assert.Empty(_reader.Armings);
    }

    [Fact]
    public async Task An_expired_poll_says_what_to_narrow_rather_than_failing()
    {
        var project = Holding();
        _reader.Expiring = true;

        var read = await Tail(project.Id, since: new TailCursor(Now, 0));

        // The five seconds bind this like every other read (ADR 0026) — and the
        // number came from this poll in the first place.
        Assert.True(read!.Expired);
        Assert.Equal([Narrowing.TimeRange], read.Narrow);
    }

    private Task<Read<Arrivals>?> Tail(
        Guid projectId, EntryFilters? filters = null, TailCursor? since = null) =>
        new TailEntries(_projects, _reader).ExecuteAsync(
            projectId,
            filters ?? EntryFilters.None,
            since,
            TestContext.Current.CancellationToken);
}
