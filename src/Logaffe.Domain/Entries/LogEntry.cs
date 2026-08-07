namespace Logaffe.Domain.Entries;

/// <summary>
/// The atomic record logaffe stores: one thing that happened in one sender.
/// </summary>
/// <remarks>
/// <para>
/// It is written once and never edited, and it leaves only by ageing out — which
/// is what lets the read path be fitted to its indexes without regard for write
/// amplification on update. <c>docs/storage.md</c> is the table this shape is,
/// column for column.
/// </para>
/// <para>
/// <b>Nothing here constructs one from a delivery.</b> The caps of
/// <c>docs/ingestion.md</c> — thirty-two kibibytes of rendered message, sixty-four
/// of exception, sixty-four properties — and the truncation that
/// <see href="https://github.com/datavisionzero/logaffe/blob/main/docs/adr/0008-an-over-long-message-is-truncated-not-refused.md">ADR 0008</see>
/// chose over refusing the entry are the ingestion path's, and they arrive with
/// it. What this type holds is the shape the table holds, and the one rule that
/// is about the shape rather than about the delivery: a promoted trace is the
/// length a trace id is, or it is not promoted at all.
/// </para>
/// </remarks>
public sealed class LogEntry
{
    /// <summary>
    /// A W3C trace id is sixteen bytes. CLEF delivers it as thirty-two hex
    /// characters and logaffe stores the bytes, which halves the column and
    /// every key in the trace index.
    /// </summary>
    public const int TraceIdLength = 16;

    /// <summary>A W3C span id is eight bytes.</summary>
    public const int SpanIdLength = 8;

    private readonly byte[]? _traceId;
    private readonly byte[]? _spanId;

    /// <summary>
    /// Handed out by the ingestion path before the row is written, not by a
    /// database sequence.
    /// </summary>
    /// <remarks>
    /// Binary <c>COPY</c> carries the value with the row, so a sequence would
    /// mean a <c>nextval</c> per entry or a round trip per batch on the hottest
    /// path in the product. An installation is a single writer, so a counter
    /// seeded from the high-water mark at startup is all this needs. Gaps are
    /// irrelevant — nothing counts these and nothing assumes they are dense —
    /// but uniqueness is not: the cursor of <c>docs/querying.md</c> is
    /// <c>(event_time, id)</c> and is only total because of it.
    /// </remarks>
    public required long Id { get; init; }

    /// <summary>
    /// The project the entry belongs to, which is the identity that survives
    /// every rename rather than the name.
    /// </summary>
    /// <remarks>
    /// It leads every index on this table, because nothing in this product reads
    /// across projects and an index that did not lead with it would make every
    /// query pay for every other project's entries.
    /// </remarks>
    public required Guid ProjectId { get; init; }

    /// <summary>
    /// The moment the sender says it happened, and what the product orders by
    /// (ADR 0007).
    /// </summary>
    public required DateTimeOffset EventTime { get; init; }

    /// <summary>
    /// The moment the installation received the batch, and what retention counts
    /// from — the only one of the two clocks a sender cannot get wrong.
    /// </summary>
    public required DateTimeOffset ReceiptTime { get; init; }

    /// <summary>
    /// The severity the sender assigned. An entry that named none arrives here
    /// as <see cref="Levels.WhenAbsent"/>, decided on the way in rather than on
    /// the way out.
    /// </summary>
    public required Level Level { get; init; }

    /// <summary>
    /// The promoted <c>SourceContext</c>: absent unless the sender delivered it.
    /// Filtering by it is the one filter that separates application output from
    /// framework noise, and it is the filter an operator reaches for first.
    /// </summary>
    public string? LoggerName { get; init; }

    /// <summary>
    /// The promoted <c>instance</c>: which running copy of the sender this came
    /// from. Absent unless the sender delivered it, and an application that
    /// supplies none of the promoted properties is fully supported.
    /// </summary>
    public string? Instance { get; init; }

    /// <summary>
    /// The promoted <c>TraceId</c>, as the sixteen bytes it is.
    /// </summary>
    /// <remarks>
    /// Storing the bytes makes the field self-validating: promotion requires a
    /// well-formed value, so a sender delivering something that is not a trace
    /// id keeps it as an ordinary property, where it is stored and displayed
    /// like any other, rather than having it silently accepted into a column
    /// that promises a shape it does not have.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// The value is not <see cref="TraceIdLength"/> bytes.
    /// </exception>
    public byte[]? TraceId
    {
        get => _traceId;
        init => _traceId = OfLength(value, TraceIdLength, nameof(TraceId));
    }

    /// <summary>
    /// The promoted <c>SpanId</c>, as the eight bytes it is. It carries no index
    /// of its own — the trace is what gathers the entries of one request.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The value is not <see cref="SpanIdLength"/> bytes.
    /// </exception>
    public byte[]? SpanId
    {
        get => _spanId;
        init => _spanId = OfLength(value, SpanIdLength, nameof(SpanId));
    }

    /// <summary>
    /// The message as the sender wrote it, which is always a template — a plain
    /// sentence is one with no placeholders in it. Kept for fidelity, never
    /// shown and never searched (ADR 0005).
    /// </summary>
    public required string MessageTemplate { get; init; }

    /// <summary>
    /// What the operator reads and what a search matches, computed once when the
    /// entry arrived rather than each time it is read.
    /// </summary>
    public required string RenderedMessage { get; init; }

    /// <summary>
    /// Whatever the runtime produced, stack trace and all, stored as delivered
    /// and never parsed. It carries no index deliberately: a stack trace is
    /// kilobytes where a rendered message is a line (ADR 0028).
    /// </summary>
    public string? Exception { get; init; }

    /// <summary>
    /// The properties the sender delivered, as the JSON object they arrived as.
    /// </summary>
    /// <remarks>
    /// Held as the text of that object rather than as a parsed structure,
    /// because nothing in this product reads inside it: nothing indexes
    /// properties and no filter reaches them (ADR 0010). It is written by the
    /// ingestion path and handed back to a consumer as data.
    /// <para>
    /// The column is <c>jsonb</c>, which keeps the properties and not the text:
    /// key order and whitespace do not survive the round trip. That is not the
    /// truncation exception to "stored as delivered" — no value is lost — and
    /// nothing in the product depends on the order a sender wrote its keys in.
    /// </para>
    /// </remarks>
    public string? Properties { get; init; }

    /// <summary>
    /// Whether <see cref="RenderedMessage"/> is shorter than what arrived. This
    /// and <see cref="ExceptionTruncated"/> are the one place on the ingestion
    /// path where what is stored differs from what was delivered, which is why
    /// they are flags on the row rather than something inferred later.
    /// </summary>
    public required bool MessageTruncated { get; init; }

    /// <summary>Whether <see cref="Exception"/> is shorter than what arrived.</summary>
    public required bool ExceptionTruncated { get; init; }

    private static byte[]? OfLength(byte[]? value, int length, string name) =>
        value is null || value.Length == length
            ? value
            : throw new ArgumentException($"A {name} is {length} bytes.", name);
}
