using Logaffe.Domain.Entries;

namespace Logaffe.Domain.Queries;

/// <summary>
/// The narrowings a query is made of: seven of them, every one of them optional,
/// and all of the ones that are set applying at once.
/// </summary>
/// <remarks>
/// <para>
/// <b>They only remove entries and none adds any</b>, and they combine with
/// <c>AND</c> alone — no <c>OR</c>, no negation and no grouping
/// (<see href="https://github.com/datavisionzero/logaffe/blob/main/docs/adr/0011-filters-only-narrow-and-only-with-and.md">ADR 0011</see>).
/// Two questions that need an <c>OR</c> between them are two queries. What that
/// buys is that every query has an obvious meaning, an agent cannot formulate
/// one that parses but asks nonsense, and this type never grows a grammar.
/// </para>
/// <para>
/// It is the same set for both consumers. The operator's screen and the MCP
/// tools narrow with this and nothing else, which is what keeps the two from
/// being two surfaces that drift (<c>docs/querying.md</c>).
/// </para>
/// <para>
/// <b>Nothing here is a permission.</b> The project a query runs inside is not
/// on this type: it is the caller's, checked where credentials are, and passing
/// it as one filter among seven would make the separation the product promises a
/// value somebody could leave unset.
/// </para>
/// </remarks>
public sealed record EntryFilters
{
    /// <summary>The filter set that removes nothing, which is a project's whole page.</summary>
    public static readonly EntryFilters None = new();

    private readonly byte[]? _traceId;

    /// <summary>
    /// The earliest <see cref="LogEntry.EventTime"/> an entry may carry.
    /// </summary>
    /// <remarks>
    /// <b>Event time and not receipt time</b> — the operator asking what happened
    /// between 10:00 and 10:05 means when it happened, not when it arrived
    /// (ADR 0007). The live tail is the one read whose cursor runs on the other
    /// clock, and this still narrows what it answers: a view showing the last
    /// quarter of an hour does not start showing an entry from this morning
    /// because it was delivered late.
    /// </remarks>
    public DateTimeOffset? From { get; init; }

    /// <summary>
    /// The latest event time an entry may carry, and it is <b>exclusive</b>: a
    /// range is half-open so that consecutive ranges neither overlap nor leave a
    /// gap, which is what lets an operator page a day an hour at a time without
    /// meeting an entry twice.
    /// </summary>
    public DateTimeOffset? Until { get; init; }

    /// <summary>
    /// The lowest severity an entry may carry — a <b>threshold</b> rather than a
    /// selection, because "Warning and above" is the question people actually
    /// ask and it is one control instead of six. The numbers of
    /// <see cref="Level"/> are ordered for exactly this, and the partial index
    /// of <c>docs/storage.md</c> serves the threshold operators reach for most.
    /// </summary>
    public Level? MinimumLevel { get; init; }

    /// <summary>
    /// The sender copy an entry came from, matched whole and exactly. It is a
    /// value read off an entry that is already on the screen rather than picked
    /// from a list of the ones in use
    /// (<see href="https://github.com/datavisionzero/logaffe/blob/main/docs/adr/0029-filter-values-come-from-the-entries-not-from-a-list.md">ADR 0029</see>).
    /// </summary>
    public string? Instance { get; init; }

    /// <summary>
    /// The promoted <c>SourceContext</c>, matched whole and exactly — this is
    /// the filter that cuts framework noise from application output, and it is
    /// the one an operator reaches for first.
    /// </summary>
    /// <remarks>
    /// Whole rather than by prefix: <c>Microsoft.*</c> is the obvious ask and it
    /// is a grammar in one column, which is the thing ADR 0011 declined. The
    /// index is fitted to equality, and a prefix would not use it as it stands.
    /// </remarks>
    public string? LoggerName { get; init; }

    /// <summary>
    /// The trace whose entries are wanted, as the sixteen bytes a trace id is —
    /// which is what gathers the entries of one request.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The value is not <see cref="LogEntry.TraceIdLength"/> bytes.
    /// </exception>
    public byte[]? TraceId
    {
        get => _traceId;
        init => _traceId = value is null || value.Length == LogEntry.TraceIdLength
            ? value
            : throw new ArgumentException(
                $"A {nameof(TraceId)} is {LogEntry.TraceIdLength} bytes.", nameof(TraceId));
    }

    /// <summary>
    /// The free text, matched as a case-insensitive substring of the rendered
    /// message. Its own type, because the three-character minimum is a rule and
    /// not a check an endpoint remembers to make (ADR 0025).
    /// </summary>
    public SearchText? Search { get; init; }

    /// <summary>
    /// The free text matched the same way against the exception, and against
    /// nothing else. It is separate from <see cref="Search"/> because the
    /// exception is where the bytes are, and no index serves it — which makes it
    /// the one filter that can be slow, deliberately (ADR 0028).
    /// </summary>
    public SearchText? ExceptionText { get; init; }

    /// <summary>
    /// Whether the range asks for a period that exists. An <see cref="Until"/>
    /// at or before <see cref="From"/> is not an empty answer but a malformed
    /// question, and the difference matters: a caller told "no entries" would go
    /// looking for a delivery problem.
    /// </summary>
    /// <remarks>
    /// It is the only way this type can be wrong. Every other field is either
    /// absent, or a value that validated itself on the way in.
    /// </remarks>
    public bool HasARange => From is null || Until is null || From < Until;

    /// <summary>
    /// Whether anything at all is set — which is what tells the empty project
    /// apart from the filter set that matched nothing, the two readings
    /// <c>docs/ui.md</c> says must never be shown for each other.
    /// </summary>
    public bool Narrows =>
        From is not null
        || Until is not null
        || MinimumLevel is not null
        || Instance is not null
        || LoggerName is not null
        || _traceId is not null
        || Search is not null
        || ExceptionText is not null;
}
