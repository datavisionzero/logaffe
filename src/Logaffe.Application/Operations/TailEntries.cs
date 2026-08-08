using Logaffe.Application.Ports;
using Logaffe.Domain.Entries;
using Logaffe.Domain.Queries;

namespace Logaffe.Application.Operations;

/// <summary>
/// What one poll of a live tail answers: what arrived, where the next poll
/// resumes, and whether there was more than one poll may carry.
/// </summary>
/// <param name="Entries">
/// The entries that arrived since the cursor the poll was given, in the view's
/// order — newest first by event time, identity breaking ties — so that a caller
/// places them into the page it holds without re-sorting it. Empty is the
/// ordinary answer: most polls of a quiet project return nothing.
/// </param>
/// <param name="Next">
/// The position the following poll resumes after. <b>Always a cursor, never
/// absent</b>: a poll that answered nothing hands back the one it was given, so
/// that following the logs is a loop over the last answer rather than a state
/// the caller has to keep.
/// </param>
/// <param name="More">
/// Whether this poll filled its cap and there is more waiting. <b>Nothing is
/// lost when it is set</b> — the cursor names a position in the arrival order
/// and the entries behind it are still ahead of it — but the interval is not
/// keeping up, and the caller asks again rather than waiting it out.
/// </param>
public sealed record Arrivals(IReadOnlyList<LogEntry> Entries, TailCursor Next, bool More);

/// <summary>
/// The live tail: what has arrived since the last poll.
/// </summary>
/// <remarks>
/// <para>
/// It is the one request this product repeats on its own, and it asks a
/// different question from the filtered page. <b>The cursor runs on receipt time
/// while the view it feeds stays ordered by event time</b>
/// (ADR 0009). A sender that was disconnected delivers entries whose event times
/// are older than what the tail has already shown; a cursor on event time would
/// never return them, and the outage being watched would be the one thing the
/// live view omits. Because the cursor runs on the receipt they arrive, and
/// because the answer is ordered by event time they take their place among the
/// entries they belong with.
/// </para>
/// <para>
/// <b>It narrows with the same filters as everything else and adds no rule of
/// its own.</b> A tail is a filter set that is being watched rather than a mode:
/// the same seven narrowings, the same three-character minimum, the same five
/// seconds (ADR 0026) — the number that came from this poll in the first place.
/// The event-time range narrows it too, so a view showing the last quarter of an
/// hour does not begin showing this morning's entries because they were
/// delivered late; what the range does not do here is decide what is new.
/// </para>
/// <para>
/// <b>Nothing watches.</b> A poll happens because a screen is open, and this
/// answers one poll. There is no subscription, no push, and nothing that fires —
/// <c>VISION.md</c> is explicit that passive continuous monitoring is not part of
/// the product, and the tail is the operator's screen keeping itself current.
/// </para>
/// </remarks>
public sealed class TailEntries(IProjects projects, IEntryReader entries)
{
    /// <summary>
    /// One poll, or <c>null</c> when there is no such project — which is what a
    /// project deleted in another tab looks like to the view still tailing it.
    /// </summary>
    /// <remarks>
    /// <b>The first poll arms the tail and returns nothing.</b> A tail with no
    /// cursor is a view that has just loaded its page, and what it needs is the
    /// position to watch from — answering it the newest entries instead would
    /// hand back what the page it is sitting on already shows. So the poll with
    /// no cursor answers no entries and the end of the project's arrival order,
    /// and every poll after it is the same call with the cursor it was last
    /// given.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// The range asks for a period that does not exist. A caller taking filters
    /// from a person says so before it gets here; this is the backstop.
    /// </exception>
    public async Task<Read<Arrivals>?> ExecuteAsync(
        Guid projectId,
        EntryFilters filters,
        TailCursor? since,
        CancellationToken cancellationToken)
    {
        if (!filters.HasARange)
        {
            throw new ArgumentException("A time range ends after it starts.", nameof(filters));
        }

        var project = await projects.FindAsync(projectId, cancellationToken);
        if (project is null)
        {
            return null;
        }

        try
        {
            if (since is null)
            {
                var start = await entries.NewestArrivalAsync(project.Id, cancellationToken);

                // A project holding nothing starts before everything, because
                // everything delivered from here on is something that arrived
                // while the view was watching.
                return Read<Arrivals>.Of(
                    new Arrivals([], start ?? TailCursor.Beginning, More: false));
            }

            var arrived = await entries.ArrivalsAsync(
                project.Id, filters, since, cancellationToken);

            return Read<Arrivals>.Of(new Arrivals(
                arrived, Resuming(arrived, since), arrived.Count == Page.Size));
        }
        catch (ReadExpiredException)
        {
            // Not an error to report: the filters are what has to change, and
            // this is where the caller is told which of them (ADR 0026).
            return Read<Arrivals>.RanOut(filters);
        }
    }

    /// <summary>
    /// Where the next poll resumes: the furthest along the arrival order of what
    /// this one answered, and the cursor it was given when it answered nothing.
    /// </summary>
    /// <remarks>
    /// Not the last entry of the answer. That order is the view's, on the event
    /// clock, and the entry that arrived last is anywhere in it — a late
    /// delivery is the case the tail exists for, and it lands at the bottom.
    /// Taking the wrong one would hand out a position ahead of entries the next
    /// poll has not seen, which is the one way this loses an entry for good.
    /// </remarks>
    private static TailCursor Resuming(IReadOnlyList<LogEntry> arrived, TailCursor since)
    {
        var next = since;

        foreach (var entry in arrived)
        {
            var arrival = TailCursor.After(entry);
            if (arrival.IsAfter(next))
            {
                next = arrival;
            }
        }

        return next;
    }
}
