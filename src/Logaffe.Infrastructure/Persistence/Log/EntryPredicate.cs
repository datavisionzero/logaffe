using System.Globalization;
using System.Text;
using Dapper;
using Logaffe.Domain.Entries;
using Logaffe.Domain.Queries;

namespace Logaffe.Infrastructure.Persistence.Log;

/// <summary>
/// The seven filters, as the <c>where</c> clause the page and the count share.
/// </summary>
/// <remarks>
/// <para>
/// One composer for both, because they narrow identically and a second copy of
/// this is how a count comes to answer a different question from the page it is
/// counting. It only ever appends <c>and</c>: there is no <c>or</c>, no negation
/// and no grouping to build (ADR 0011), which is why this is a list of clauses
/// and not a tree.
/// </para>
/// <para>
/// <b>A filter that is not set contributes no clause at all.</b> The obvious
/// alternative — <c>(@from is null or event_time &gt;= @from)</c> for each of
/// them — is one statement instead of many, and it is the wrong one: the planner
/// cannot see through a null test to the index, so the ordinary query with two
/// filters set would be planned as though all seven were.
/// </para>
/// </remarks>
internal static class EntryPredicate
{
    /// <summary>
    /// Postgres's default escape character in <c>like</c>, which is what the
    /// three characters below are escaped with.
    /// </summary>
    private const char Escape = '\\';

    /// <summary>
    /// The clause and the values it reads, for one project's entries narrowed by
    /// <paramref name="filters"/>.
    /// </summary>
    public static (string Where, DynamicParameters Parameters) For(
        Guid projectId, EntryFilters filters)
    {
        var parameters = new DynamicParameters();
        var where = new StringBuilder();

        // First and always. It leads every index on this table, and nothing in
        // this product reads across projects.
        parameters.Add("projectId", projectId);
        where.Append("project_id = @projectId");

        // Event time and not receipt time: what the operator means by "between
        // 10:00 and 10:05" is when it happened (ADR 0007). Half-open, so that
        // consecutive ranges neither overlap nor leave a gap.
        if (filters.From is { } from)
        {
            parameters.Add("from", from.UtcDateTime);
            where.Append(" and event_time >= @from");
        }

        if (filters.Until is { } until)
        {
            parameters.Add("until", until.UtcDateTime);
            where.Append(" and event_time < @until");
        }

        if (filters.MinimumLevel is { } minimum)
        {
            // The one value written into the statement rather than passed
            // beside it. The partial index of docs/storage.md is defined over
            // `level >= 3`, and the planner can only match a query to it when
            // the bound is a constant it can compare — with a parameter here,
            // "Warning and above" would be planned as a scan and the index that
            // exists for the question people actually ask would never be used.
            // The value is an enum member and not input.
            where.Append(" and level >= ")
                .Append(((short)minimum).ToString(CultureInfo.InvariantCulture));
        }

        if (filters.Instance is { } instance)
        {
            parameters.Add("instance", instance);
            where.Append(" and instance = @instance");
        }

        if (filters.LoggerName is { } loggerName)
        {
            parameters.Add("loggerName", loggerName);
            where.Append(" and logger_name = @loggerName");
        }

        if (filters.TraceId is { } traceId)
        {
            parameters.Add("traceId", traceId);
            where.Append(" and trace_id = @traceId");
        }

        // Case-insensitive substring, anywhere in the rendered message and
        // including inside a word — grep and not a search engine (ADR 0010).
        // `ilike` is what the trigram index serves; a lowered comparison or a
        // regular expression would not reach it.
        if (filters.Search is { } search)
        {
            parameters.Add("search", Anywhere(search));
            where.Append(" and rendered_message ilike @search");
        }

        // The same match against the exception, which no index serves — the one
        // read that can be slow, deliberately (ADR 0028). It goes last so that
        // it rechecks a candidate set the filters above have already made small.
        if (filters.ExceptionText is { } exception)
        {
            parameters.Add("exception", Anywhere(exception));
            where.Append(" and exception ilike @exception");
        }

        return (where.ToString(), parameters);
    }

    /// <summary>
    /// A search text as the pattern that finds it wherever it occurs.
    /// </summary>
    /// <remarks>
    /// The three characters <c>like</c> reads as syntax are escaped first.
    /// Without that, a search for <c>100%</c> would match every message
    /// beginning with <c>100</c> and an operator would be told their filter
    /// found things it did not — the substring promise has to hold for the
    /// characters the pattern language happens to use.
    /// </remarks>
    private static string Anywhere(SearchText text)
    {
        var pattern = new StringBuilder(text.Value.Length + 2).Append('%');

        foreach (var character in text.Value)
        {
            if (character is Escape or '%' or '_')
            {
                pattern.Append(Escape);
            }

            pattern.Append(character);
        }

        return pattern.Append('%').ToString();
    }
}
