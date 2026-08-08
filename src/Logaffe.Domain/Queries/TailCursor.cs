using Logaffe.Domain.Entries;

namespace Logaffe.Domain.Queries;

/// <summary>
/// What a live tail has already seen: the receipt time and the identity of the
/// latest entry that has arrived for it.
/// </summary>
/// <remarks>
/// <para>
/// <b>The receipt clock, and not the event clock the view is ordered by</b>
/// (<see href="https://github.com/datavisionzero/logaffe/blob/main/docs/adr/0009-the-tail-follows-the-receipt-the-view-keeps-the-order-of-events.md">ADR 0009</see>).
/// A sender that was disconnected delivers entries whose event times are older
/// than what the tail has already shown, and a cursor on event time would never
/// return them — the outage being watched would be the one thing the live view
/// omits. This position moves with delivery, so they arrive.
/// </para>
/// <para>
/// <b>It is its own type and not <see cref="EntryCursor"/> with another
/// meaning.</b> The two name positions in two different orders, and one type
/// carrying either would be a value whose clock is a matter of where it came
/// from — which is the confusion that ADR costs an index to avoid. They share
/// the form they are written in and nothing else.
/// </para>
/// <para>
/// <b>The identity is what makes it total.</b> A batch is received in one act
/// and its entries can share a receipt time to the microsecond, so a cursor on
/// the timestamp alone would either repeat that batch on the next poll or skip
/// the rest of it. The pair is a key in <c>(project_id, receipt_time, id)</c>,
/// which is the index <c>docs/storage.md</c> already carries for this and for
/// the retention sweep.
/// </para>
/// </remarks>
/// <param name="ReceiptTime">When the latest entry the tail has seen arrived.</param>
/// <param name="Id">That entry's identity, which breaks the ties the time leaves.</param>
public sealed record TailCursor(DateTimeOffset ReceiptTime, long Id)
{
    /// <summary>
    /// Before anything: the position a tail on a project holding no entries
    /// starts from.
    /// </summary>
    /// <remarks>
    /// An empty project has no arrival to start after, and everything delivered
    /// from now on is something that arrived while the view was watching — which
    /// is exactly what a position before every entry answers. It is a cursor
    /// like any other rather than an absent one, so that a poll always hands
    /// back the position of the next poll and a caller never has to hold a state
    /// of its own.
    /// </remarks>
    public static readonly TailCursor Beginning = new(DateTimeOffset.MinValue, 0);

    /// <summary>
    /// The cursor that resumes after <paramref name="entry"/> arrived.
    /// </summary>
    public static TailCursor After(LogEntry entry) => new(entry.ReceiptTime, entry.Id);

    /// <summary>
    /// Whether this position is later in the arrival order than
    /// <paramref name="other"/>.
    /// </summary>
    /// <remarks>
    /// A poll answers its entries in the view's order, which is the event clock,
    /// so the last of them is not the latest to have arrived. Where the next
    /// poll resumes is the furthest of them along this order, and that is what
    /// this is for.
    /// </remarks>
    public bool IsAfter(TailCursor other) =>
        ReceiptTime > other.ReceiptTime
        || (ReceiptTime == other.ReceiptTime && Id > other.Id);

    /// <summary>The opaque form.</summary>
    /// <inheritdoc cref="EntryCursor.ToString"/>
    public override string ToString() => CursorText.Of(ReceiptTime.UtcTicks, Id);

    /// <summary>
    /// Reads a cursor back, answering <c>false</c> when
    /// <paramref name="value"/> is not one.
    /// </summary>
    /// <remarks>
    /// <b>Absent is not malformed.</b> No cursor is a tail that has not started
    /// yet, and what that poll answers is the position to start from.
    /// </remarks>
    public static bool TryParse(string? value, out TailCursor? cursor)
    {
        cursor = null;

        if (!CursorText.TryRead(value, out var position))
        {
            return false;
        }

        if (position is { } read)
        {
            cursor = new TailCursor(new DateTimeOffset(read.Ticks, TimeSpan.Zero), read.Id);
        }

        return true;
    }
}
