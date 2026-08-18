using System.ComponentModel;
using System.Text.Json.Nodes;
using Logaffe.Api.Queries;
using Logaffe.Application.Operations;
using Logaffe.Application.Ports;
using Logaffe.Domain.Entries;
using Logaffe.Domain.Queries;

namespace Logaffe.Api.Mcp;

/// <summary>
/// Which shape a search answers entries in, chosen per call.
/// </summary>
public enum Verbosity
{
    /// <summary>
    /// Event time, level, logger name, instance and the rendered message. The
    /// default, because a broad search that silently spends an agent's whole
    /// context is worse than one that needs a second call.
    /// </summary>
    Compact = 0,

    /// <summary>Both clocks, the template, the properties, the exception and the truncation.</summary>
    Full,
}

/// <summary>
/// How many entries one call may answer with, which is this adapter's number and
/// not <see cref="Page.Size"/>.
/// </summary>
/// <remarks>
/// These sit above the page: a tool pages the use case underneath it until it is
/// full or the log runs out. They bound one answer to an agent, and the reason
/// the two shapes have different ones is that the entries behind them are
/// different sizes — a full entry drags its properties and its exception along.
/// </remarks>
public static class AgentCap
{
    public const int Compact = 200;

    public const int Full = 50;

    public static int Of(Verbosity verbosity) =>
        verbosity is Verbosity.Full ? Full : Compact;
}

/// <summary>
/// One project, as <c>list_projects</c> answers with it.
/// </summary>
/// <remarks>
/// The group and the last receipt are the two a project may not have, and they
/// are declared optional for the reason <see cref="AgentJson"/> gives: a project
/// in no group leaves the field out, and a schema requiring it would have the
/// client throw the whole list away.
/// </remarks>
public sealed record AgentProject
{
    [Description("Names this project in every other tool.")]
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    [Description(
        "The group this project is listed under, or absent when it is in none. "
        + "It is the operator's own word for a set of projects that belong "
        + "together — one product's environments, one customer's applications — "
        + "and it is what resolves a request naming one of those. It narrows "
        + "nothing: every tool reads one project, and a group is not one.")]
    public string? Group { get; init; }

    [Description("How long the project keeps its entries, counted from receipt.")]
    public required int RetentionDays { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    [Description(
        "When this project last received an entry, or absent when it never has. "
        + "Whether it is still being delivered to, which is the cheapest health "
        + "question there is about one.")]
    public DateTimeOffset? LastReceivedAt { get; init; }

    /// <param name="group">
    /// The name of the group the project points at, which the caller resolves —
    /// a project carries the identity, and the name is on the group list.
    /// </param>
    public static AgentProject Of(ListedProject project, string? group) => new()
    {
        Id = project.Id,
        Name = project.Name,
        Group = group,
        RetentionDays = project.Retention.Days,
        CreatedAt = project.CreatedAt,
        LastReceivedAt = project.LastReceivedAt,
    };
}

/// <summary>Every project the installation holds.</summary>
public sealed record ProjectsAnswer(IReadOnlyList<AgentProject> Projects);

/// <summary>
/// One entry, in whichever of the two shapes was asked for.
/// </summary>
/// <remarks>
/// <para>
/// <b>Named fields, and nothing folded into a sentence</b> (ADR 0012). The
/// rendered message is a field carrying text and nothing in it is interpreted:
/// no markdown, no transcript, no formatting applied on the way out. This type
/// and <c>Http/EntryEndpoints</c> are the two places that claim is enforced, and
/// therefore the two places it can be audited.
/// </para>
/// <para>
/// The compact shape is the five fields <c>docs/mcp.md</c> names, plus the
/// identity. The identity is not one of the five, but <c>get_entry</c> is asked
/// with it and the whole point of the compact shape is the follow-up after it —
/// an agent that saw a promising line and cannot name it has been given a list
/// it can only read.
/// </para>
/// <para>
/// The fields the compact shape leaves out are absent rather than null, so a
/// broad search does not spend the context it was made compact to save.
/// <see cref="SearchAnswer.Verbosity"/> is what says which shape an answer is
/// in, so that a missing exception is never mistaken for an entry that has one.
/// </para>
/// </remarks>
public sealed record AgentEntry
{
    public required long Id { get; init; }

    public required DateTimeOffset EventTime { get; init; }

    public required string Level { get; init; }

    public string? LoggerName { get; init; }

    public string? Instance { get; init; }

    public required string Message { get; init; }

    public DateTimeOffset? ReceiptTime { get; init; }

    public string? Trace { get; init; }

    public string? Span { get; init; }

    public string? MessageTemplate { get; init; }

    public string? Exception { get; init; }

    /// <summary>
    /// The properties the sender delivered, as the object they arrived as.
    /// </summary>
    /// <remarks>
    /// <b>Declared as the object it is rather than as any JSON at all.</b> A
    /// <see cref="JsonNode"/> here exports as the boolean schema <c>true</c> —
    /// legal JSON Schema meaning "anything", and refused by clients that hold
    /// tool schemas to being schema objects. One field like that costs the whole
    /// tool list, because a client that cannot read the list does not get to keep
    /// the three tools it could read.
    /// </remarks>
    public JsonObject? Properties { get; init; }

    public bool? MessageTruncated { get; init; }

    public bool? ExceptionTruncated { get; init; }

    public static AgentEntry Of(LogEntry entry, Verbosity verbosity) =>
        verbosity is Verbosity.Full ? Full(entry) : Compact(entry);

    private static AgentEntry Compact(LogEntry entry) => new()
    {
        Id = entry.Id,
        EventTime = entry.EventTime,
        Level = entry.Level.ToString(),
        LoggerName = entry.LoggerName,
        Instance = entry.Instance,
        Message = entry.RenderedMessage,
    };

    public static AgentEntry Full(LogEntry entry) => new()
    {
        Id = entry.Id,
        EventTime = entry.EventTime,
        ReceiptTime = entry.ReceiptTime,
        Level = entry.Level.ToString(),
        LoggerName = entry.LoggerName,
        Instance = entry.Instance,
        Trace = Hex(entry.TraceId),
        Span = Hex(entry.SpanId),
        MessageTemplate = entry.MessageTemplate,
        Message = entry.RenderedMessage,
        Exception = entry.Exception,

        // The object it was delivered as, handed back as one. Nothing here reads
        // inside it and nothing renders it (ADR 0010, ADR 0012).
        Properties = entry.Properties is null
            ? null
            : JsonNode.Parse(entry.Properties)?.AsObject(),
        MessageTruncated = entry.MessageTruncated,
        ExceptionTruncated = entry.ExceptionTruncated,
    };

    private static string? Hex(byte[]? value) =>
        value is null ? null : Convert.ToHexStringLower(value);
}

/// <summary>
/// What a search answers with.
/// </summary>
/// <remarks>
/// The verbosity, the entries and the cap are on every answer; the total, the
/// cursor and the narrowings are each on some of them, and are optional for the
/// reason <see cref="AgentJson"/> gives.
/// </remarks>
public sealed record SearchAnswer
{
    public required Verbosity Verbosity { get; init; }

    public required IReadOnlyList<AgentEntry> Entries { get; init; }

    /// <summary>
    /// How many entries the filters match in the project — not how many are in
    /// <see cref="Entries"/>. An agent that receives fifty entries and is not
    /// told there were nine thousand will answer as though there were fifty, and
    /// that is the quietest way this product could produce a wrong answer. Absent
    /// only on a read that expired, which answered nothing to count against.
    /// </summary>
    public long? Matched { get; init; }

    /// <summary>
    /// Whether this answer stopped at the cap with more still to read.
    /// <see cref="Cursor"/> is where to continue from.
    /// </summary>
    public required bool Capped { get; init; }

    /// <summary>
    /// What to hand the next call to carry on, or absent when this answer reached
    /// the end of the matches. Opaque: it is passed back unread.
    /// </summary>
    public string? Cursor { get; init; }

    /// <summary>
    /// Present when the read used up its five seconds, and then the only thing
    /// present besides the verbosity. These are the adjustments to make, in the
    /// order to try them — values rather than a sentence, because the operator's
    /// screen writes the sentence and an agent gets the fact (ADR 0012).
    /// </summary>
    public IReadOnlyList<Narrowing>? Narrow { get; init; }

    public static SearchAnswer Of(
        Verbosity verbosity,
        IReadOnlyList<LogEntry> entries,
        long matched,
        bool capped,
        EntryCursor? cursor) => new()
    {
        Verbosity = verbosity,
        Entries = [.. entries.Select(entry => AgentEntry.Of(entry, verbosity))],
        Matched = matched,
        Capped = capped,
        Cursor = cursor?.ToString(),
    };

    /// <summary>
    /// A read that met the five seconds, and what to change so that the next one
    /// does not (ADR 0026).
    /// </summary>
    public static SearchAnswer RanOut(
        Verbosity verbosity, IReadOnlyList<Narrowing> narrow) => new()
    {
        Verbosity = verbosity,
        Entries = [],
        Capped = false,
        Narrow = narrow,
    };
}

/// <summary>
/// One row of a count.
/// </summary>
public sealed record CountedGroupAnswer
{
    /// <summary>
    /// The value that was grouped by, or absent for the entries carrying no
    /// value in the grouped column — a row that is every entry with no logger
    /// name on it, and which is a row and not an omission.
    /// </summary>
    public string? Value { get; init; }

    public required long Entries { get; init; }
}

/// <summary>
/// What a count answers with: a number, or the rows it was broken into.
/// </summary>
/// <remarks>
/// <para>
/// <b>The two are exclusive, and an ungrouped count is the number.</b>
/// <c>docs/mcp.md</c> promises a count answered "as a number instead of as the
/// entries", and a one-row list under a <c>groups</c> key, whose single row
/// carries no value to be grouped under, is not that — it is the shape of the
/// grouped answer with the grouping taken out, and it asks the agent to reach
/// through a collection for something the tool was asked for directly. Which
/// one is present says which question was asked, the way
/// <see cref="SearchAnswer.Narrow"/> says a read ran out.
/// </para>
/// <para>
/// Which is also why not one of the three is required: each of them is absent
/// from some answer this tool makes, and the schema has to say so
/// (<see cref="AgentJson"/>).
/// </para>
/// </remarks>
public sealed record CountAnswer
{
    /// <summary>
    /// How many entries matched, on a count that was not grouped.
    /// </summary>
    public long? Entries { get; init; }

    /// <summary>
    /// One row per value, on a count that was — and absent rather than empty when
    /// it was not.
    /// </summary>
    public IReadOnlyList<CountedGroupAnswer>? Groups { get; init; }

    /// <inheritdoc cref="SearchAnswer.Narrow"/>
    public IReadOnlyList<Narrowing>? Narrow { get; init; }

    public static CountAnswer Of(IReadOnlyList<CountedGroup> groups, Grouping grouping) =>
        grouping is Grouping.None

            // An ungrouped count is one row of SQL and always exactly one, so
            // this is the number itself rather than a row that has to be read
            // out of a list of one.
            ? new() { Entries = groups.Single().Entries }
            : new()
            {
                Groups =
                [
                    .. groups.Select(group => new CountedGroupAnswer
                    {
                        Value = EntryFilterText.NamedGroup(grouping, group.Value),
                        Entries = group.Entries,
                    }),
                ],
            };

    /// <inheritdoc cref="SearchAnswer.RanOut"/>
    public static CountAnswer RanOut(IReadOnlyList<Narrowing> narrow) =>
        new() { Narrow = narrow };
}
