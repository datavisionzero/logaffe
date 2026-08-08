using System.Text.Json.Nodes;
using Logaffe.Application.Operations;
using Logaffe.Application.Ports;
using Logaffe.Domain.Entries;
using Logaffe.Domain.Queries;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Logaffe.Api.Http;

/// <summary>
/// The seven filters, as they arrive in an address.
/// </summary>
/// <remarks>
/// They are query parameters and not a body, because <c>docs/ui.md</c> puts the
/// filters that make up a view in its address: a log view is a thing an operator
/// links a colleague to, reloads, and finds again in their history.
/// </remarks>
/// <param name="From">
/// The earliest event time, inclusive. Event time and not receipt time — the
/// operator asking what happened between 10:00 and 10:05 means when it happened.
/// </param>
/// <param name="Until">The latest event time, exclusive, so that ranges tile.</param>
/// <param name="MinimumLevel">
/// A threshold and not a selection: <c>Warning</c> means warning and above. Both
/// spellings of the ends of the scale are accepted, as on the way in.
/// </param>
/// <param name="Instance">The sender copy, matched whole.</param>
/// <param name="LoggerName">The promoted <c>SourceContext</c>, matched whole.</param>
/// <param name="Trace">The trace id, as the thirty-two hex characters CLEF carries.</param>
/// <param name="Search">
/// A case-insensitive substring of the rendered message, at least three
/// characters.
/// </param>
/// <param name="Exception">The same, against the exception, which is its own filter.</param>
public sealed record EntryFiltersRequest(
    [FromQuery] DateTimeOffset? From,
    [FromQuery] DateTimeOffset? Until,
    [FromQuery] string? MinimumLevel,
    [FromQuery] string? Instance,
    [FromQuery] string? LoggerName,
    [FromQuery] string? Trace,
    [FromQuery] string? Search,
    [FromQuery] string? Exception);

/// <summary>
/// One entry as a page carries it: what a line of the list is read from.
/// </summary>
/// <remarks>
/// It carries no exception and no properties. A page of a hundred entries each
/// dragging a four-megabyte stack trace behind it is the response nobody asked
/// for, and the entry that is opened is fetched whole by its identity.
/// <see cref="HasException"/> is what the list shows a mark for.
/// </remarks>
public sealed record ListedEntryResponse(
    long Id,
    DateTimeOffset EventTime,
    string Level,
    string? LoggerName,
    string? Instance,
    string? Trace,
    string Message,
    bool MessageTruncated,
    bool HasException);

/// <summary>
/// One page, and where the next one starts.
/// </summary>
/// <param name="Next">
/// The cursor to hand back to get the following page, or <c>null</c> when this
/// was the last one. It is opaque: it is passed on unread.
/// </param>
/// <remarks>
/// <b>There is no total here</b>, deliberately. Counting the matches of a
/// substring search on every page for a number nobody asked for is the wrong
/// default; the count is its own request.
/// </remarks>
public sealed record EntryPageResponse(IEnumerable<ListedEntryResponse> Entries, string? Next);

/// <summary>
/// One entry in full: the follow-up after a compact search.
/// </summary>
/// <param name="Properties">
/// The properties the sender delivered, as the object they arrived as — data and
/// never prose (ADR 0012).
/// </param>
public sealed record EntryResponse(
    long Id,
    DateTimeOffset EventTime,
    DateTimeOffset ReceiptTime,
    string Level,
    string? LoggerName,
    string? Instance,
    string? Trace,
    string? Span,
    string MessageTemplate,
    string Message,
    string? Exception,
    JsonNode? Properties,
    bool MessageTruncated,
    bool ExceptionTruncated);

/// <summary>
/// One row of a count.
/// </summary>
/// <param name="Value">
/// The value that was grouped by, or <c>null</c> both for the ungrouped count
/// and for the entries that carry no value in the grouped column.
/// </param>
public sealed record CountedGroupResponse(string? Value, long Entries);

/// <summary>What a count answers with.</summary>
public sealed record CountResponse(IEnumerable<CountedGroupResponse> Groups);

/// <summary>
/// A read that used up its five seconds, and what to change so that the next one
/// does not.
/// </summary>
/// <param name="Narrow">
/// The adjustments to try, in order. Named values rather than a sentence,
/// because the operator's screen writes the sentence and an agent gets the fact
/// (ADR 0012).
/// </param>
public sealed record ReadExpiredResponse(IEnumerable<string> Narrow);

/// <summary>
/// Reading one project's entries, over HTTP.
/// </summary>
/// <remarks>
/// <para>
/// These three sit on the use cases of <c>docs/querying.md</c> and add no query
/// behaviour of their own — which is what makes the promise that the operator
/// and the agent share one surface structural rather than a matter of
/// discipline. When the MCP adapter arrives it calls the same three.
/// </para>
/// <para>
/// <b>This is where log content becomes a response</b>, and therefore where
/// ADR 0012 is enforced and auditable: entries reach a consumer as structured
/// values in named fields, never as rendered markdown, a formatted transcript,
/// or text folded into an instruction. Nothing below concatenates a message into
/// anything.
/// </para>
/// <para>
/// Everything here is behind the operator's session and everything is a
/// <c>GET</c>: the read path writes nothing, and a filter set belongs in an
/// address (<c>docs/ui.md</c>).
/// </para>
/// </remarks>
public static class EntryEndpoints
{
    public static IEndpointRouteBuilder MapEntries(this IEndpointRouteBuilder endpoints)
    {
        var operatorSurface = endpoints
            .MapGroup("/projects/{id:guid}/entries")
            .RequireAuthorization()
            .RequireRateLimiting(PublicRateLimits.Operator);

        operatorSurface.MapGet(string.Empty, async (
                Guid id,
                [AsParameters] EntryFiltersRequest request,
                [FromQuery] string? cursor,
                SearchEntries search,
                CancellationToken cancellationToken) =>
            {
                if (!TryRead(request, out var filters, out var invalid))
                {
                    return invalid;
                }

                // Refused rather than ignored. Starting at the top because a
                // cursor did not parse would silently hand back a page the
                // caller has already read.
                if (!EntryCursor.TryParse(cursor, out var after))
                {
                    return NotACursor();
                }

                var read = await search.ExecuteAsync(id, filters, after, cancellationToken);

                if (read is null)
                {
                    return Results.NotFound();
                }

                return read.Expired
                    ? Expired(read.Narrow)
                    : Results.Ok(new EntryPageResponse(
                        read.Answer!.Entries.Select(Listed),
                        read.Answer.Next?.ToString()));
            })
            .WithName("SearchEntries")
            .WithSummary("One page of a project's entries, newest first by event time.")
            .Produces<EntryPageResponse>()
            .Produces<ReadExpiredResponse>(StatusCodes.Status408RequestTimeout)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();

        operatorSurface.MapGet("/count", async (
                Guid id,
                [AsParameters] EntryFiltersRequest request,
                [FromQuery] string? groupBy,
                [FromQuery] string? bucket,
                CountEntries count,
                CancellationToken cancellationToken) =>
            {
                if (!TryRead(request, out var filters, out var invalid))
                {
                    return invalid;
                }

                if (!TryReadGrouping(groupBy, out var grouping))
                {
                    return NotAGrouping();
                }

                if (!TryReadBucket(bucket, out var bucketed))
                {
                    return NotABucket();
                }

                var read = await count.ExecuteAsync(
                    id, filters, grouping, bucketed, cancellationToken);

                if (read is null)
                {
                    return Results.NotFound();
                }

                return read.Expired
                    ? Expired(read.Narrow)
                    : Results.Ok(new CountResponse(
                        read.Answer!.Select(group => new CountedGroupResponse(
                            Named(grouping, group.Value), group.Entries))));
            })
            .WithName("CountEntries")
            .WithSummary("How many entries a filter set matches, optionally grouped.")
            .Produces<CountResponse>()
            .Produces<ReadExpiredResponse>(StatusCodes.Status408RequestTimeout)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();

        operatorSurface.MapGet("/{entryId:long}", async (
                Guid id,
                long entryId,
                ReadEntry read,
                CancellationToken cancellationToken) =>
            {
                var entry = await read.ExecuteAsync(id, entryId, cancellationToken);

                // An entry that aged out between the page and the click looks
                // like this, and so does an identity somebody guessed.
                return entry is null ? Results.NotFound() : Results.Ok(Shown(entry));
            })
            .WithName("ReadEntry")
            .WithSummary("One entry in full, with its exception and its properties.")
            .Produces<EntryResponse>()
            .Produces(StatusCodes.Status404NotFound);

        return endpoints;
    }

    /// <summary>
    /// The filters as the domain holds them, or the problem to answer with.
    /// </summary>
    /// <remarks>
    /// Every rule below is one the domain refuses as a backstop. A caller taking
    /// filters from a person says which one they got wrong first, which is what
    /// the operator's screen needs and what an agent correcting itself needs.
    /// </remarks>
    private static bool TryRead(
        EntryFiltersRequest request, out EntryFilters filters, out IResult invalid)
    {
        filters = EntryFilters.None;
        invalid = Results.Empty;

        Level? minimum = null;
        if (request.MinimumLevel is not null)
        {
            if (!Levels.TryParse(request.MinimumLevel, out var level))
            {
                invalid = Problem("minimumLevel", "A level is one of the six severities.");
                return false;
            }

            minimum = level;
        }

        byte[]? traceId = null;
        if (request.Trace is not null)
        {
            if (!TryReadTrace(request.Trace, out traceId))
            {
                invalid = Problem(
                    "trace",
                    $"A trace is {LogEntry.TraceIdLength * 2} hexadecimal characters.");
                return false;
            }
        }

        if (!TryReadText(request.Search, out var search))
        {
            invalid = Problem("search", TooShort);
            return false;
        }

        if (!TryReadText(request.Exception, out var exception))
        {
            invalid = Problem("exception", TooShort);
            return false;
        }

        filters = new EntryFilters
        {
            From = request.From,
            Until = request.Until,
            MinimumLevel = minimum,
            Instance = request.Instance,
            LoggerName = request.LoggerName,
            TraceId = traceId,
            Search = search,
            ExceptionText = exception,
        };

        if (!filters.HasARange)
        {
            // A malformed question rather than an empty answer: a caller told
            // "no entries" would go looking for a delivery problem.
            invalid = Problem("until", "A time range ends after it starts.");
            return false;
        }

        return true;
    }

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
        // (ADR 0025), so it is refused where it was typed rather than run.
        if (!SearchText.TryCreate(value, out var created))
        {
            return false;
        }

        text = created;
        return true;
    }

    private static bool TryReadGrouping(string? value, out Grouping grouping) =>
        value is null
            ? (grouping = Grouping.None) is Grouping.None
            : Enum.TryParse(value, ignoreCase: true, out grouping)
              && Enum.IsDefined(grouping);

    private static bool TryReadBucket(string? value, out TimeBucket bucket) =>
        value is null
            // The middle of the three, which is the one that reads a day.
            ? (bucket = TimeBucket.Hour) is TimeBucket.Hour
            : Enum.TryParse(value, ignoreCase: true, out bucket)
              && Enum.IsDefined(bucket);

    /// <summary>
    /// A grouped value as it is read. The level is stored as a number and
    /// grouped as one, and this is where it becomes the name the rest of the
    /// contract uses.
    /// </summary>
    private static string? Named(Grouping grouping, string? value) =>
        grouping is Grouping.Level && short.TryParse(value, out var level)
            ? ((Level)level).ToString()
            : value;

    private static ListedEntryResponse Listed(LogEntry entry) => new(
        entry.Id,
        entry.EventTime,
        entry.Level.ToString(),
        entry.LoggerName,
        entry.Instance,
        Hex(entry.TraceId),
        entry.RenderedMessage,
        entry.MessageTruncated,
        entry.Exception is not null);

    private static EntryResponse Shown(LogEntry entry) => new(
        entry.Id,
        entry.EventTime,
        entry.ReceiptTime,
        entry.Level.ToString(),
        entry.LoggerName,
        entry.Instance,
        Hex(entry.TraceId),
        Hex(entry.SpanId),
        entry.MessageTemplate,
        entry.RenderedMessage,
        entry.Exception,

        // The object it was delivered as, handed back as one. Nothing here reads
        // inside it and nothing renders it (ADR 0010, ADR 0012).
        entry.Properties is null ? null : JsonNode.Parse(entry.Properties),
        entry.MessageTruncated,
        entry.ExceptionTruncated);

    private static string? Hex(byte[]? value) =>
        value is null ? null : Convert.ToHexStringLower(value);

    /// <summary>
    /// Not a database error and not a failure: the filters are what has to
    /// change, and these are the ones to change (ADR 0026).
    /// </summary>
    private static IResult Expired(IReadOnlyList<Narrowing> narrow) => Results.Json(
        new ReadExpiredResponse(narrow.Select(one => one.ToString())),
        statusCode: StatusCodes.Status408RequestTimeout);

    private static readonly string TooShort =
        $"A search text is at least {SearchText.MinimumLength} characters.";

    private static IResult Problem(string parameter, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [parameter] = [message] });

    private static IResult NotACursor() =>
        Problem("cursor", "A cursor is one handed back by a previous page.");

    private static IResult NotAGrouping() => Problem(
        "groupBy",
        $"A count is grouped by one of {string.Join(", ", Enum.GetNames<Grouping>())}.");

    private static IResult NotABucket() => Problem(
        "bucket",
        $"A time bucket is one of {string.Join(", ", Enum.GetNames<TimeBucket>())}.");
}
