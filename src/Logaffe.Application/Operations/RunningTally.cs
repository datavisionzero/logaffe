using System.Collections.Concurrent;
using Logaffe.Domain.Projects;

namespace Logaffe.Application.Operations;

/// <summary>
/// The counter the ingestion path moves and the flush writes down: what has
/// arrived for each project, in each hour, since the last time it was taken.
/// </summary>
/// <remarks>
/// <para>
/// <b>It is in memory because the installation is a single writer</b> — one
/// container, one ingestion endpoint. That is the same fact
/// <c>docs/storage.md</c> already leans on for entry identities, and it is what
/// makes a number held here sufficient where a second writer would need the
/// database to arbitrate.
/// </para>
/// <para>
/// <b>Nothing on the delivery path waits for anything here.</b> Recording is an
/// interlocked add against a dictionary entry; it takes no lock the length of a
/// batch, does no I/O and answers nothing. This is the hottest path in the
/// product and the adoption barrier <c>VISION.md</c> judges it by, so a tally
/// that cost it a measurable amount would be the wrong tally rather than a cost
/// to work around.
/// </para>
/// <para>
/// <b>A restart loses whatever has not been flushed.</b> Up to a minute of
/// counts, gone, with nothing reconciling them afterwards (ADR 0047). It is not
/// the record of what arrived — <c>log_entry</c> is, and it is written before
/// any of this moves.
/// </para>
/// <para>
/// It is a singleton, like <c>DummySecret</c> and for the same reason: what it
/// holds is the installation's rather than a request's, and one per scope would
/// be one per delivery.
/// </para>
/// </remarks>
public sealed class RunningTally
{
    private ConcurrentDictionary<OpenHour, Counts> _open = new();

    /// <summary>
    /// Counts a batch that has been stored, into the hour its receipt falls in.
    /// </summary>
    /// <remarks>
    /// Called after the write rather than before it: what this counts is what
    /// the table took, so a delivery the store threw on counts for nothing.
    /// </remarks>
    public void Record(
        Guid projectId, DateTimeOffset receiptTime, long entries, long atErrorOrAbove)
    {
        if (entries == 0)
        {
            return;
        }

        var counts = _open.GetOrAdd(
            new OpenHour(projectId, Tallying.HourOf(receiptTime)), _ => new Counts());

        Interlocked.Add(ref counts.Entries, entries);
        Interlocked.Add(ref counts.AtErrorOrAbove, atErrorOrAbove);
    }

    /// <summary>
    /// Takes everything counted so far and starts again from nothing.
    /// </summary>
    /// <remarks>
    /// The exchange is what makes this safe against deliveries arriving during
    /// it: a batch that has already found its entry in the outgoing dictionary
    /// adds to that, and lands in this answer or in the gap between the two.
    /// The gap is at most one batch and it is the same gap a restart leaves,
    /// which is the accuracy ADR 0047 settled for.
    /// </remarks>
    public IReadOnlyList<TallyIncrement> Take()
    {
        var taken = Interlocked.Exchange(ref _open, new ConcurrentDictionary<OpenHour, Counts>());

        return
        [
            .. taken.Select(open => new TallyIncrement
            {
                ProjectId = open.Key.ProjectId,
                Hour = open.Key.Hour,
                Entries = Interlocked.Read(ref open.Value.Entries),
                AtErrorOrAbove = Interlocked.Read(ref open.Value.AtErrorOrAbove),
            }),
        ];
    }

    /// <summary>
    /// Puts increments back after a flush that stored none of them.
    /// </summary>
    /// <remarks>
    /// The write is one transaction, so a flush that threw left nothing behind
    /// and these are owed to the next one. Without this a database that was
    /// briefly unreachable would cost the same as a restart — and a failure
    /// nobody restarted for is the more likely of the two.
    /// </remarks>
    public void PutBack(IReadOnlyList<TallyIncrement> increments)
    {
        foreach (var increment in increments)
        {
            var counts = _open.GetOrAdd(
                new OpenHour(increment.ProjectId, increment.Hour), _ => new Counts());

            Interlocked.Add(ref counts.Entries, increment.Entries);
            Interlocked.Add(ref counts.AtErrorOrAbove, increment.AtErrorOrAbove);
        }
    }

    /// <summary>One project's one hour, which is the key the row has.</summary>
    private readonly record struct OpenHour(Guid ProjectId, DateTimeOffset Hour);

    /// <summary>
    /// Mutable and by reference, because the two numbers are moved with
    /// interlocked adds against the fields themselves — a record here would mean
    /// a compare-and-swap loop over a new object per batch.
    /// </summary>
    private sealed class Counts
    {
        public long Entries;
        public long AtErrorOrAbove;
    }
}
