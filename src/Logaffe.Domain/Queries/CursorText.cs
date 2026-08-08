using System.Buffers;
using System.Buffers.Binary;
using System.Buffers.Text;

namespace Logaffe.Domain.Queries;

/// <summary>
/// The written form a cursor takes on the wire: a timestamp and an identity, as
/// one opaque string.
/// </summary>
/// <remarks>
/// <para>
/// This surface has two positions — where a page left off, on the event clock,
/// and what a poll has already seen, on the receipt clock — and they are
/// deliberately two types, because one type answering to two clocks is the
/// confusion ADR 0009 exists to prevent. What they are not is two formats: both
/// are a pair of eight-byte numbers, and a second copy of the encoding is how
/// one of them comes to survive a query string while the other does not.
/// </para>
/// <para>
/// <b>It is opaque to whoever holds it.</b> What a caller gets back is a string
/// it hands over unread, so that the pair inside can change without every
/// consumer of the contract changing with it — and so that nobody builds one by
/// hand and discovers on the day the order changes that they were relying on the
/// format.
/// </para>
/// </remarks>
internal static class CursorText
{
    /// <summary>Ticks and identity, which is what the encoded form holds.</summary>
    private const int Bytes = sizeof(long) * 2;

    /// <summary>
    /// The pair as base64url, so that it survives a query string without
    /// escaping and survives being pasted out of an address bar.
    /// </summary>
    public static string Of(long ticks, long id)
    {
        Span<byte> bytes = stackalloc byte[Bytes];

        BinaryPrimitives.WriteInt64BigEndian(bytes, ticks);
        BinaryPrimitives.WriteInt64BigEndian(bytes[sizeof(long)..], id);

        return Base64Url.EncodeToString(bytes);
    }

    /// <summary>
    /// Reads the pair back, answering <c>false</c> when
    /// <paramref name="value"/> is not one and <c>null</c> when there was none.
    /// </summary>
    /// <remarks>
    /// <b>Absent is not malformed.</b> No cursor is where a read starts, which
    /// is the ordinary case and not a caller getting something wrong, so an
    /// empty value reads as none.
    /// </remarks>
    public static bool TryRead(string? value, out (long Ticks, long Id)? position)
    {
        position = null;

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

        position = (ticks, BinaryPrimitives.ReadInt64BigEndian(bytes[sizeof(long)..]));

        return true;
    }
}
