using System.Buffers;
using System.Buffers.Binary;
using System.Buffers.Text;
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
/// <b>It is opaque on the wire.</b> What a caller gets back is a string it hands
/// over unread, so that the pair inside can change without every consumer of the
/// contract changing with it — and so that nobody builds one by hand and
/// discovers on the day the order changes that they were relying on the format.
/// A cursor that does not parse is refused rather than ignored: paging on from a
/// position nobody chose is the one failure a cursor exists to prevent.
/// </para>
/// </remarks>
/// <param name="EventTime">The event time of the last entry on the page.</param>
/// <param name="Id">That entry's identity, which breaks the ties the time leaves.</param>
public sealed record EntryCursor(DateTimeOffset EventTime, long Id)
{
    /// <summary>Ticks and identity, which is what the encoded form holds.</summary>
    private const int Bytes = sizeof(long) * 2;

    /// <summary>
    /// The cursor that resumes after <paramref name="entry"/>, which is the last
    /// one on a page.
    /// </summary>
    public static EntryCursor After(LogEntry entry) => new(entry.EventTime, entry.Id);

    /// <summary>
    /// The opaque form, in base64url so that it survives a query string without
    /// escaping and survives being pasted out of an address bar.
    /// </summary>
    public override string ToString()
    {
        Span<byte> bytes = stackalloc byte[Bytes];

        // As the instant it is. The offset a cursor was written with is not part
        // of the position it names, and keeping it would make two cursors to the
        // same row that do not compare equal.
        BinaryPrimitives.WriteInt64BigEndian(bytes, EventTime.UtcTicks);
        BinaryPrimitives.WriteInt64BigEndian(bytes[sizeof(long)..], Id);

        return Base64Url.EncodeToString(bytes);
    }

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

        if (string.IsNullOrEmpty(value))
        {
            return true;
        }

        Span<byte> bytes = stackalloc byte[Bytes];

        // Through the status rather than through TryDecodeFromChars, which
        // throws on a character that is not base64 instead of answering that it
        // is not — and what arrives here is a query string a person edited.
        //
        // The length is checked as well as the decoding: a shorter input decodes
        // happily into the front of the span and would leave the rest of the
        // pair as whatever the stack held.
        var decoded = Base64Url.DecodeFromChars(
            value, bytes, out var read, out var written, isFinalBlock: true);

        if (decoded is not OperationStatus.Done || read != value.Length || written != Bytes)
        {
            return false;
        }

        var ticks = BinaryPrimitives.ReadInt64BigEndian(bytes);
        if (ticks < DateTimeOffset.MinValue.UtcTicks || ticks > DateTimeOffset.MaxValue.UtcTicks)
        {
            return false;
        }

        cursor = new EntryCursor(
            new DateTimeOffset(ticks, TimeSpan.Zero),
            BinaryPrimitives.ReadInt64BigEndian(bytes[sizeof(long)..]));

        return true;
    }
}
