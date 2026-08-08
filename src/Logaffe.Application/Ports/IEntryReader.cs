using Logaffe.Domain.Entries;
using Logaffe.Domain.Queries;

namespace Logaffe.Application.Ports;

/// <summary>
/// One row of a grouped count: a value the entries were grouped by, and how many
/// carried it.
/// </summary>
/// <param name="Value">
/// The value of the grouping column, or <c>null</c> for the entries that carry
/// none — a logger name is absent unless the sender delivered it, and the
/// entries without one are a group like any other rather than entries that
/// vanish from the total.
/// </param>
/// <param name="Entries">How many entries fell into it.</param>
public sealed record CountedGroup(string? Value, long Entries);

/// <summary>
/// Reading the entries a project holds: the filtered page, the count, and one
/// entry in full.
/// </summary>
/// <remarks>
/// <para>
/// This is the other half of <see cref="IEntries"/> and it is deliberately a
/// port of its own. That one is the write and the sweep — a batch in, rows out,
/// nothing handed back — and it says so. This one hands entries back, is fitted
/// to the indexes <c>docs/storage.md</c> claims, and is the surface both
/// consumers meet: the operator's screen and the MCP tools call these three and
/// nothing else, which is what keeps them from being two surfaces that drift
/// (<c>docs/querying.md</c>).
/// </para>
/// <para>
/// <b>Every method takes the project.</b> A query always runs inside one, and
/// there is no reading across them — not as a permission but as an absence: none
/// of these can be asked a question that spans two.
/// </para>
/// <para>
/// <b>The five seconds are the implementation's.</b> ADR 0026 binds this
/// surface, and the thing that can enforce it is the thing holding the
/// statement; a caller counting on its own would be measuring a wait rather than
/// stopping a query. What reaches the layer above is a
/// <see cref="OperationCanceledException"/> from a read that ran out of them,
/// which the use cases turn into what to narrow.
/// </para>
/// </remarks>
public interface IEntryReader
{
    /// <summary>
    /// One page of the entries matching <paramref name="filters"/>, newest first
    /// by event time with the identity breaking ties, resuming after
    /// <paramref name="after"/> when there is one.
    /// </summary>
    /// <remarks>
    /// It answers at most <see cref="Page.Size"/> entries and carries no total:
    /// counting the matches of a substring search is a scan, and paying for it
    /// on every page for a number nobody asked for is the wrong default. Whether
    /// there is another page is read off the length of this one.
    /// </remarks>
    Task<IReadOnlyList<LogEntry>> PageAsync(
        Guid projectId,
        EntryFilters filters,
        EntryCursor? after,
        CancellationToken cancellationToken);

    /// <summary>
    /// How many entries match <paramref name="filters"/>, in the groups
    /// <paramref name="grouping"/> asks for — one row for
    /// <see cref="Grouping.None"/>, and one per value otherwise.
    /// </summary>
    /// <remarks>
    /// This is the operation most likely to meet the five seconds, because it is
    /// the only one that cannot stop early: a page stops at its limit, a count
    /// has to visit every match.
    /// </remarks>
    Task<IReadOnlyList<CountedGroup>> CountAsync(
        Guid projectId,
        EntryFilters filters,
        Grouping grouping,
        TimeBucket bucket,
        CancellationToken cancellationToken);

    /// <summary>
    /// One entry by its identity, or <c>null</c> when the project holds no such
    /// entry.
    /// </summary>
    /// <remarks>
    /// The project is asked for as well as the identity, and not because the
    /// identity is not unique — it is. It is asked for so that an entry cannot
    /// be reached from a project it does not belong to by guessing a number,
    /// which would be the one hole in a separation the rest of the product keeps.
    /// </remarks>
    Task<LogEntry?> FindAsync(Guid projectId, long id, CancellationToken cancellationToken);
}
