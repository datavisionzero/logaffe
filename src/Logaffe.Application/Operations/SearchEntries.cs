using Logaffe.Application.Ports;
using Logaffe.Domain.Entries;
using Logaffe.Domain.Queries;

namespace Logaffe.Application.Operations;

/// <summary>
/// One page of entries, and where the next one starts.
/// </summary>
/// <param name="Entries">
/// At most <see cref="Page.Size"/> of them, newest first by event time with the
/// identity breaking ties.
/// </param>
/// <param name="Next">
/// The cursor the following page resumes after, or <c>null</c> when this page
/// was the last one.
/// </param>
/// <remarks>
/// <b>No total.</b> Counting the matches of a substring search on every page for
/// a number nobody asked for is the wrong default; a count is its own act.
/// </remarks>
public sealed record EntryPage(IReadOnlyList<LogEntry> Entries, EntryCursor? Next);

/// <summary>
/// The filtered page: the read this whole product is the ingestion path for.
/// </summary>
/// <remarks>
/// <para>
/// It is <b>one surface for both consumers</b>. The operator in the web UI and
/// the agent over MCP call this, with the same filters and the same order, and
/// neither is given a thinner version of the other's view — two surfaces over
/// the same data drift apart, and the difference is discovered by whoever is
/// debugging at the time (<c>docs/querying.md</c>).
/// </para>
/// <para>
/// <b>The next cursor is read off a page that filled.</b> A short page is the
/// last one, and asking for one more entry than a page holds to decide it would
/// make every page pay for the answer to a question only the last page has. The
/// cost of being wrong is one empty page at the end of a project that happened
/// to hold a multiple of the page size, which is a page that costs an index seek
/// and returns nothing.
/// </para>
/// </remarks>
public sealed class SearchEntries(IProjects projects, IEntryReader entries)
{
    /// <summary>
    /// The page, or <c>null</c> when there is no such project — which is what a
    /// project deleted in another tab looks like, and what keeps the entries a
    /// deletion left behind unreachable while the sweep is still taking them
    /// (ADR 0019).
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The range asks for a period that does not exist. A caller taking filters
    /// from a person says so before it gets here; this is the backstop.
    /// </exception>
    public async Task<Read<EntryPage>?> ExecuteAsync(
        Guid projectId,
        EntryFilters filters,
        EntryCursor? after,
        CancellationToken cancellationToken)
    {
        if (!filters.HasARange)
        {
            throw new ArgumentException("A time range ends after it starts.", nameof(filters));
        }

        var project = await projects.FindAsync(projectId, cancellationToken);
        if (project is null)
        {
            return null;
        }

        IReadOnlyList<LogEntry> page;
        try
        {
            page = await entries.PageAsync(project.Id, filters, after, cancellationToken);
        }
        catch (ReadExpiredException)
        {
            // Not an error to report: the filters are what has to change, and
            // this is where the caller is told which of them (ADR 0026).
            return Read<EntryPage>.RanOut(filters);
        }

        var next = page.Count == Page.Size ? EntryCursor.After(page[^1]) : null;

        return Read<EntryPage>.Of(new EntryPage(page, next));
    }
}
