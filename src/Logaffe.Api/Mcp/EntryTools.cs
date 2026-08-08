using System.ComponentModel;
using Logaffe.Api.Queries;
using Logaffe.Application.Operations;
using Logaffe.Domain.Entries;
using Logaffe.Domain.Queries;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Logaffe.Api.Mcp;

/// <summary>
/// The three reads, as an agent calls them.
/// </summary>
/// <remarks>
/// <para>
/// <b>They add no query behaviour of their own.</b> The filters, the order, the
/// cursor, the count and the five seconds are all decided in
/// <c>Logaffe.Application</c> and <c>Logaffe.Domain</c>, and these call them —
/// the same three use cases the operator's screen calls, so that the two
/// consumers cannot drift into two views of the same project
/// (<c>docs/querying.md</c>). What is decided here is a shape and a cap, and
/// nothing else is allowed to be.
/// </para>
/// <para>
/// <b>The descriptions are the only prose in this adapter</b>, and they are
/// about the tool rather than about anything a sender logged. Entries
/// themselves reach the agent as named fields and never as a sentence
/// (ADR 0012); nothing below concatenates a message into anything.
/// </para>
/// <para>
/// There is no tail here. An agent looks because the operator asked, so there is
/// no subscription, no poll and nothing delivered without a call — the fourth
/// read on this surface is the operator's alone (<c>docs/mcp.md</c>).
/// </para>
/// </remarks>
[McpServerToolType]
public static class EntryTools
{
    [McpServerTool(
        Name = "search_entries",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        """
        The log entries of one project matching a set of filters, newest first by
        event time. Filters only ever narrow and they all apply at once; leaving
        one out does not narrow by it.

        The answer always says how many entries matched in total and whether it
        stopped at its cap — up to 200 compact entries or 50 full ones. When it
        was capped, `cursor` continues from where it stopped, and `matched` is
        how many the filters match altogether rather than how many came back.

        A read gets five seconds. One that uses them up comes back with `narrow`
        instead of entries: the adjustments to make, in the order to try them.
        """)]
    public static async Task<SearchAnswer> SearchAsync(
        SearchEntries pages,
        CountEntries counts,
        [Description("The project to read, as list_projects gives it.")]
        Guid projectId,
        [Description(
            "compact is event time, level, logger name, instance and the message. "
            + "full adds both clocks, the message template, the properties, the "
            + "exception and the truncation flags, and is capped lower for it.")]
        Verbosity verbosity = Verbosity.Compact,
        [Description("The earliest event time, inclusive.")]
        DateTimeOffset? from = null,
        [Description("The latest event time, exclusive, so that ranges tile.")]
        DateTimeOffset? until = null,
        [Description(
            "A threshold and not a selection: Warning means warning and above. "
            + "One of Verbose, Debug, Information, Warning, Error, Fatal.")]
        string? minimumLevel = null,
        [Description("One running copy of a sender, matched whole.")]
        string? instance = null,
        [Description("The logger the entry came from, matched whole.")]
        string? loggerName = null,
        [Description("A trace id, as 32 hexadecimal characters.")]
        string? trace = null,
        [Description(
            "A case-insensitive substring of the rendered message, at least "
            + "three characters. It behaves like grep, not like a search engine.")]
        string? search = null,
        [Description("The same, against the exception text, which is its own filter.")]
        string? exception = null,
        [Description("The cursor a previous search handed back. Pass it on unread.")]
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var filters = Read(
            from, until, minimumLevel, instance, loggerName, trace, search, exception);

        // Refused rather than ignored. Starting at the top because a cursor did
        // not parse would silently hand back entries the caller has already read
        // and let it conclude the log repeats itself.
        if (!EntryCursor.TryParse(cursor, out var after))
        {
            throw new McpException("cursor: A cursor is one handed back by a previous search.");
        }

        var cap = AgentCap.Of(verbosity);
        var taken = new List<LogEntry>(cap);
        var position = after;
        var capped = false;

        // The cap sits above the page, so this fills it from as many pages as it
        // takes. Every one of them is the use case's own page — this decides how
        // many entries an agent is handed at once and nothing about which ones.
        while (true)
        {
            var read = await pages.ExecuteAsync(projectId, filters, position, cancellationToken);
            if (read is null)
            {
                throw NoSuchProject(projectId);
            }

            if (read.Expired)
            {
                return SearchAnswer.RanOut(verbosity, read.Narrow);
            }

            foreach (var entry in read.Answer!.Entries)
            {
                if (taken.Count == cap)
                {
                    capped = true;
                    break;
                }

                taken.Add(entry);
            }

            if (capped)
            {
                break;
            }

            position = read.Answer.Next;

            // A short page is the last one, so an answer that stops here is the
            // whole of what was left and was not capped by anything.
            if (position is null)
            {
                break;
            }

            if (taken.Count == cap)
            {
                capped = true;
                break;
            }
        }

        var matched = await MatchedAsync(
            counts, projectId, filters, taken.Count, capped, after, cancellationToken);

        // No count, no answer. The narrowings are a function of the filters the
        // count ran with, which are these, so this is the same list the use case
        // put on its own expired read.
        return matched is null
            ? SearchAnswer.RanOut(verbosity, ReadLimit.WhatToNarrow(filters))
            : SearchAnswer.Of(
                verbosity,
                taken,
                matched.Value,
                capped,
                capped ? EntryCursor.After(taken[^1]) : null);
    }

    [McpServerTool(
        Name = "count_entries",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        """
        How many entries of one project match a set of filters, answered as a
        number instead of as the entries. Optionally broken down by level, logger
        name, instance, or a bucket of event time.

        This is what turns "were there critical errors in the last three days"
        into an answer rather than forty thousand rows. It is also the read most
        likely to use up its five seconds, because it cannot stop early — one
        that does comes back with `narrow` instead of groups.
        """)]
    public static async Task<CountAnswer> CountAsync(
        CountEntries count,
        [Description("The project to count in, as list_projects gives it.")]
        Guid projectId,
        [Description("What to break the number down by, or none for the plain count.")]
        Grouping groupBy = Grouping.None,
        [Description(
            "The bucket size when grouping by time. Buckets are aligned to the "
            + "clock rather than to the range asked for.")]
        TimeBucket bucket = TimeBucket.Hour,
        [Description("The earliest event time, inclusive.")]
        DateTimeOffset? from = null,
        [Description("The latest event time, exclusive, so that ranges tile.")]
        DateTimeOffset? until = null,
        [Description(
            "A threshold and not a selection: Warning means warning and above. "
            + "One of Verbose, Debug, Information, Warning, Error, Fatal.")]
        string? minimumLevel = null,
        [Description("One running copy of a sender, matched whole.")]
        string? instance = null,
        [Description("The logger the entry came from, matched whole.")]
        string? loggerName = null,
        [Description("A trace id, as 32 hexadecimal characters.")]
        string? trace = null,
        [Description(
            "A case-insensitive substring of the rendered message, at least "
            + "three characters.")]
        string? search = null,
        [Description("The same, against the exception text, which is its own filter.")]
        string? exception = null,
        CancellationToken cancellationToken = default)
    {
        var filters = Read(
            from, until, minimumLevel, instance, loggerName, trace, search, exception);

        var read = await count.ExecuteAsync(
            projectId, filters, groupBy, bucket, cancellationToken);

        if (read is null)
        {
            throw NoSuchProject(projectId);
        }

        return read.Expired
            ? CountAnswer.RanOut(read.Narrow)
            : CountAnswer.Of(read.Answer!, groupBy);
    }

    [McpServerTool(
        Name = "get_entry",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        """
        One entry of one project by its identity, always in full: both clocks,
        the message template, every property the sender delivered, the exception
        and the truncation flags.

        This is the follow-up after a compact search — the promising line is in
        hand and what is wanted is the exception and the properties behind it.
        """)]
    public static async Task<AgentEntry> GetAsync(
        ReadEntry read,
        [Description("The project the entry belongs to, as list_projects gives it.")]
        Guid projectId,
        [Description("The identity a search answered with.")]
        long entryId,
        CancellationToken cancellationToken)
    {
        var entry = await read.ExecuteAsync(projectId, entryId, cancellationToken);

        // An entry that aged out between the search and this call looks like
        // this, and so does an identity somebody guessed.
        if (entry is null)
        {
            throw new McpException(
                $"Project {projectId} holds no entry {entryId}. It may have aged out.");
        }

        return AgentEntry.Full(entry);
    }

    /// <summary>
    /// How many entries the filters match altogether, or <c>null</c> when
    /// finding out used up the five seconds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The count is run only when the answer does not already contain it.</b>
    /// A first call that was not capped returned every entry the filters match,
    /// so the number is the length of what it returned and a second statement
    /// over the largest table in the database would be paying to be told
    /// something already in hand. Any other case — a capped answer, or a
    /// continuation whose earlier entries are not in this one — is counted.
    /// <c>docs/querying.md</c> refuses a total beside a page for the operator;
    /// the cap is why the rule differs here, and the cap is also what makes it
    /// affordable.
    /// </para>
    /// <para>
    /// <b>A capped answer without its total is not returned.</b> If the count is
    /// what met the five seconds, the whole call comes back as an expired read
    /// with what to narrow. Handing over entries and no number would be exactly
    /// the failure the number exists to prevent, dressed as a success.
    /// </para>
    /// </remarks>
    private static async Task<long?> MatchedAsync(
        CountEntries count,
        Guid projectId,
        EntryFilters filters,
        int returned,
        bool capped,
        EntryCursor? after,
        CancellationToken cancellationToken)
    {
        if (!capped && after is null)
        {
            return returned;
        }

        // The bucket is not read for an ungrouped count; it is passed because
        // the use case takes one.
        var read = await count.ExecuteAsync(
            projectId, filters, Grouping.None, TimeBucket.Hour, cancellationToken);

        if (read is null)
        {
            // Deleted between the page and the count, which is the same answer
            // as never having existed.
            throw NoSuchProject(projectId);
        }

        return read.Expired ? null : read.Answer!.Single().Entries;
    }

    /// <summary>
    /// The filters as the domain holds them, refusing the call where one of them
    /// is not a filter.
    /// </summary>
    /// <remarks>
    /// The reading is <see cref="EntryFilterText"/>'s and is the operator's
    /// screen's as well. What is this adapter's is that a filter the caller got
    /// wrong is an error naming the argument, so that a model correcting itself
    /// has something to correct.
    /// </remarks>
    private static EntryFilters Read(
        DateTimeOffset? from,
        DateTimeOffset? until,
        string? minimumLevel,
        string? instance,
        string? loggerName,
        string? trace,
        string? search,
        string? exception)
    {
        if (EntryFilterText.TryRead(
                from,
                until,
                minimumLevel,
                instance,
                loggerName,
                trace,
                search,
                exception,
                out var filters,
                out var complaint))
        {
            return filters;
        }

        throw new McpException($"{complaint!.Parameter}: {complaint.Message}");
    }

    /// <remarks>
    /// It says the project is not there and nothing else. A project deleted in
    /// the operator's other tab looks like this, and so does one an agent
    /// invented; neither is worth telling apart.
    /// </remarks>
    private static McpException NoSuchProject(Guid projectId) =>
        new($"There is no project {projectId} in this installation. Call list_projects.");
}
