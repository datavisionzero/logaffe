using Logaffe.Application.Ports;
using Logaffe.Domain.Queries;

namespace Logaffe.Application.Operations;

/// <summary>
/// How many entries a filter set matches, optionally broken down.
/// </summary>
/// <remarks>
/// <para>
/// This is what turns <i>were there critical errors in the last three days</i>
/// into an answer rather than forty thousand rows, and it is the same request
/// for both consumers: the agent calls it to answer a question, the operator
/// calls it when a number is what they want rather than a page.
/// </para>
/// <para>
/// <b>It is always asked for.</b> Nothing calls this to decorate a page — a page
/// carries no total, deliberately — so the scan it can become is one somebody
/// chose to pay for (<c>docs/querying.md</c>).
/// </para>
/// <para>
/// <b>It is the read most likely to meet the five seconds</b>, because it is the
/// only one that cannot stop early: a page stops at its limit, a count has to
/// visit every match. The narrowing that helps it is the time range, and that is
/// what an expired one comes back saying.
/// </para>
/// </remarks>
public sealed class CountEntries(IProjects projects, IEntryReader entries)
{
    /// <summary>
    /// The groups, or <c>null</c> when there is no such project.
    /// </summary>
    /// <remarks>
    /// <see cref="Grouping.None"/> answers one group whose value is
    /// <c>null</c> — the plain number — rather than a shape of its own, so that
    /// a caller reads every count the same way and a screen showing a grouped
    /// count is showing the ungrouped one with one row.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// The range asks for a period that does not exist.
    /// </exception>
    public async Task<Read<IReadOnlyList<CountedGroup>>?> ExecuteAsync(
        Guid projectId,
        EntryFilters filters,
        Grouping grouping,
        TimeBucket bucket,
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

        try
        {
            var counted = await entries.CountAsync(
                project.Id, filters, grouping, bucket, cancellationToken);

            return Read<IReadOnlyList<CountedGroup>>.Of(counted);
        }
        catch (ReadExpiredException)
        {
            return Read<IReadOnlyList<CountedGroup>>.RanOut(filters);
        }
    }
}
