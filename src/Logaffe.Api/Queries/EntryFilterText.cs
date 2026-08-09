using Logaffe.Domain.Entries;
using Logaffe.Domain.Queries;

namespace Logaffe.Api.Queries;

/// <summary>
/// What is wrong with one filter, and which one it is.
/// </summary>
/// <param name="Parameter">
/// The name the caller wrote it under, so that the answer points at the field
/// rather than at the request.
/// </param>
public sealed record FilterComplaint(string Parameter, string Message);

/// <summary>
/// The filters, the grouping and the bucket as a caller wrote them, read back
/// into the values the domain holds.
/// </summary>
/// <remarks>
/// <para>
/// It sits beside the two adapters rather than inside either of them. Both take
/// the same filters — <c>docs/querying.md</c> holds that the operator and the
/// agent meet one surface — and two copies of this would be two readings of what
/// a level, a trace or a three-character search text is, discovered as a
/// difference by whoever was debugging at the time. What each adapter keeps for
/// itself is how it says <i>no</i>: the operator's screen gets a validation
/// problem, the agent gets a tool error, and both are told the same thing about
/// the same field.
/// </para>
/// <para>
/// Every rule below is one the domain refuses as a backstop. It is read again
/// here so that a caller taking filters from a person, or a model composing
/// them, is told which one they got wrong first.
/// </para>
/// </remarks>
public static class EntryFilterText
{
    /// <summary>
    /// The filters as the domain holds them, or the first thing wrong with
    /// them, for a caller whose level arrives as text.
    /// </summary>
    /// <remarks>
    /// This is the query string's overload. A level written into one is a
    /// spelling that may or may not be a level, so it is read here and refused
    /// here — <see cref="Levels.TryParse"/> forgives the two names
    /// <c>Microsoft.Extensions.Logging</c> uses, which is a kindness to somebody
    /// typing rather than a second vocabulary. A caller that already holds a
    /// <see cref="Level"/> takes the overload below and meets none of this.
    /// </remarks>
    public static bool TryRead(
        DateTimeOffset? from,
        DateTimeOffset? until,
        string? minimumLevel,
        string? instance,
        string? loggerName,
        string? trace,
        string? search,
        string? exception,
        out EntryFilters filters,
        out FilterComplaint? complaint)
    {
        Level? minimum = null;
        if (minimumLevel is not null)
        {
            if (!Levels.TryParse(minimumLevel, out var level))
            {
                filters = EntryFilters.None;
                complaint = new FilterComplaint(
                    "minimumLevel", "A level is one of the six severities.");
                return false;
            }

            minimum = level;
        }

        return TryRead(
            from, until, minimum, instance, loggerName, trace, search, exception,
            out filters, out complaint);
    }

    /// <summary>
    /// The same, for a caller whose level is already one.
    /// </summary>
    /// <remarks>
    /// The agent surface declares the level as the closed set it is, so the six
    /// severities are in the tool schema and a model composing a call is told
    /// them rather than left to read them out of a sentence. There is nothing
    /// left to refuse by the time a value gets here, which is the point.
    /// </remarks>
    public static bool TryRead(
        DateTimeOffset? from,
        DateTimeOffset? until,
        Level? minimumLevel,
        string? instance,
        string? loggerName,
        string? trace,
        string? search,
        string? exception,
        out EntryFilters filters,
        out FilterComplaint? complaint)
    {
        filters = EntryFilters.None;
        complaint = null;

        byte[]? traceId = null;
        if (trace is not null && !TryReadTrace(trace, out traceId))
        {
            complaint = new FilterComplaint(
                "trace", $"A trace is {LogEntry.TraceIdLength * 2} hexadecimal characters.");
            return false;
        }

        if (!TryReadText(search, out var searchText))
        {
            complaint = new FilterComplaint("search", TooShort);
            return false;
        }

        if (!TryReadText(exception, out var exceptionText))
        {
            complaint = new FilterComplaint("exception", TooShort);
            return false;
        }

        filters = new EntryFilters
        {
            From = from,
            Until = until,
            MinimumLevel = minimumLevel,
            Instance = instance,
            LoggerName = loggerName,
            TraceId = traceId,
            Search = searchText,
            ExceptionText = exceptionText,
        };

        if (!filters.HasARange)
        {
            // A malformed question rather than an empty answer: a caller told
            // "no entries" would go looking for a delivery problem.
            complaint = new FilterComplaint("until", "A time range ends after it starts.");
            return false;
        }

        return true;
    }

    /// <summary>
    /// A grouped value as it is read. The level is stored as a number and
    /// grouped as one, and this is where it becomes the name the rest of the
    /// contract uses.
    /// </summary>
    public static string? NamedGroup(Grouping grouping, string? value) =>
        grouping is Grouping.Level && short.TryParse(value, out var level)
            ? ((Level)level).ToString()
            : value;

    /// <summary>The three-character minimum, said the same way on both surfaces.</summary>
    private static readonly string TooShort =
        $"A search text is at least {SearchText.MinimumLength} characters.";

    /// <summary>
    /// A trace as it is written on the wire, back to the bytes it is. An
    /// ill-formed one is refused rather than passed on: promotion required a
    /// well-formed value, so a filter carrying anything else could only match
    /// nothing, and answering "no entries" to a question that was never asked is
    /// the wrong answer.
    /// </summary>
    private static bool TryReadTrace(string value, out byte[]? traceId)
    {
        traceId = null;

        if (value.Length != LogEntry.TraceIdLength * 2)
        {
            return false;
        }

        try
        {
            traceId = Convert.FromHexString(value);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool TryReadText(string? value, out SearchText? text)
    {
        text = null;

        if (value is null)
        {
            return true;
        }

        // Two characters is not a narrower search, it is a scan of the project
        // (ADR 0025), so it is refused where it was written rather than run.
        if (!SearchText.TryCreate(value, out var created))
        {
            return false;
        }

        text = created;
        return true;
    }
}
