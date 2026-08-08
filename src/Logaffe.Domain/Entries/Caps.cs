using System.Text;

namespace Logaffe.Domain.Entries;

/// <summary>
/// The sizes a delivery may not exceed, and the one modification logaffe makes
/// to what it was given.
/// </summary>
/// <remarks>
/// <para>
/// These are <b>product values</b>: the same in every installation, documented in
/// <c>docs/ingestion.md</c>, and not something the operator tunes. An operator
/// who could raise them would be deciding on their own behalf what a shared
/// contract says, and every sender is already under their control.
/// </para>
/// <para>
/// The two of them behave differently on purpose. A batch over
/// <see cref="EntriesPerBatch"/> or <see cref="BatchBytes"/> is <b>refused
/// whole</b> — nothing about it can be trusted or afforded, and it is one of the
/// three cases <c>docs/ingestion.md</c> allows that in. A message or exception
/// over its own cap is <b>truncated and flagged</b>, because the entries that
/// overrun one are the four-megabyte stack traces and the dumped payloads, which
/// is to say the entries an operator is most likely to have gone looking for
/// (<see href="https://github.com/datavisionzero/logaffe/blob/main/docs/adr/0008-an-over-long-message-is-truncated-not-refused.md">ADR 0008</see>).
/// </para>
/// </remarks>
public static class Caps
{
    /// <summary>How many entries one delivery may carry.</summary>
    public const int EntriesPerBatch = 1_000;

    /// <summary>
    /// How large one delivery may be, measured <b>after</b> decompression — so
    /// that the cap cannot be walked around with a compression bomb.
    /// </summary>
    public const int BatchBytes = 5 * 1024 * 1024;

    /// <summary>How long a rendered message may be, in bytes of UTF-8.</summary>
    public const int RenderedMessageBytes = 32 * 1024;

    /// <summary>How long an exception may be, in bytes of UTF-8.</summary>
    public const int ExceptionBytes = 64 * 1024;

    /// <summary>
    /// How many properties one entry may carry beside its own fields.
    /// </summary>
    /// <remarks>
    /// Unlike the two above this is not truncated: dropping the sixty-fifth
    /// property would be a silent modification of what was delivered, and which
    /// one went would be arbitrary. An entry over it is counted invalid, which
    /// is a defect in the sending application and one the operator can fix
    /// (ADR 0006).
    /// </remarks>
    public const int PropertiesPerEntry = 64;

    /// <summary>
    /// How deep a property value may go: a scalar, or one object or array of
    /// them. Nothing in this product reads inside a property (ADR 0010), so the
    /// depth buys nothing and an unbounded one is a parser handed arbitrary
    /// nesting by an untrusted line.
    /// </summary>
    public const int PropertyNesting = 1;

    /// <summary>
    /// <paramref name="text"/> cut to <paramref name="cap"/> bytes of UTF-8,
    /// and whether cutting was necessary — which is what the entry's flag
    /// records, so that nobody reads a shortened stack trace as a complete one.
    /// </summary>
    /// <remarks>
    /// The cut lands on a character boundary rather than on a byte one. A
    /// stack trace ending in half a surrogate pair would not be text any more,
    /// and every consumer of the column — the search, the UI, MCP — would be
    /// carrying that. The encoder is what knows where the boundary is.
    /// </remarks>
    public static (string? Text, bool Truncated) CutTo(string? text, int cap)
    {
        if (text is null || Encoding.UTF8.GetByteCount(text) <= cap)
        {
            return (text, false);
        }

        // Converted rather than measured backwards: the encoder consumes whole
        // characters and stops when the next one would not fit, and how many it
        // got through is exactly the prefix that fits.
        var encoder = Encoding.UTF8.GetEncoder();
        encoder.Convert(
            text.AsSpan(),
            new byte[cap],
            flush: true,
            out var charsUsed,
            out _,
            out _);

        return (text[..charsUsed], true);
    }
}
