using Logaffe.Domain.Entries;

namespace Logaffe.Domain.Queries;

/// <summary>
/// Where a page left off: the event time and the identity of its last entry.
/// </summary>
/// <remarks>
/// <para>
/// <b>Paging is by cursor and never by offset.</b> Entries keep arriving while a
/// person reads, and an offset would skip and repeat rows as the store grows
/// underneath them — the operator would page past the entry they came for
/// without it ever having been on a screen. A cursor names a position in the
/// order rather than a distance into it, so a page five thousand entries deep
/// costs what the first one costs.
/// </para>
/// <para>
/// <b>The identity is what makes it total.</b> Two entries can carry the same
/// event time — the same millisecond of the same batch is ordinary — and a
/// cursor on the timestamp alone would either lose the entries that share it or
/// return them again. That is why <see cref="LogEntry.Id"/> is unique rather
/// than merely dense (<c>docs/storage.md</c>), and why the paging index is
/// <c>(project_id, event_time desc, id desc)</c>: this pair is a key in it.
/// </para>
/// <para>
/// <b>It runs on the event clock, which is the one the page is ordered by.</b>
/// The live tail asks a different question and has <see cref="TailCursor"/> for
/// it; the two are separate types so that a position on one clock can never be
/// handed to a read that runs on the other (ADR 0009). What they share is
/// <see cref="CursorText"/>, which is the form both are written in.
/// </para>
/// <para>
/// <b>It is opaque on the wire.</b> A cursor that does not parse is refused
/// rather than ignored: paging on from a position nobody chose is the one
/// failure a cursor exists to prevent.
/// </para>
/// </remarks>
/// <param name="EventTime">The event time of the last entry on the page.</param>
/// <param name="Id">That entry's identity, which breaks the ties the time leaves.</param>
public sealed record EntryCursor(DateTimeOffset EventTime, long Id)
{
    /// <summary>
    /// The cursor that resumes after <paramref name="entry"/>, which is the last
    /// one on a page.
    /// </summary>
    public static EntryCursor After(LogEntry entry) => new(entry.EventTime, entry.Id);

    /// <summary>The opaque form.</summary>
    /// <remarks>
    /// Written as the instant it is. The offset a cursor was written with is not
    /// part of the position it names, and keeping it would make two cursors to
    /// the same row that do not compare equal.
    /// </remarks>
    public override string ToString() => CursorText.Of(EventTime.UtcTicks, Id);

    /// <summary>
    /// Reads a cursor back, answering <c>false</c> when
    /// <paramref name="value"/> is not one.
    /// </summary>
    /// <remarks>
    /// <b>Absent is not malformed.</b> No cursor is the first page, which is the
    /// ordinary case and not a caller getting something wrong, so an empty value
    /// parses to none.
    /// </remarks>
    public static bool TryParse(string? value, out EntryCursor? cursor)
    {
        cursor = null;

        if (!CursorText.TryRead(value, out var position))
        {
            return false;
        }

        if (position is { } read)
        {
            cursor = new EntryCursor(new DateTimeOffset(read.Ticks, TimeSpan.Zero), read.Id);
        }

        return true;
    }
}
