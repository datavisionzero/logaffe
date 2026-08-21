using Logaffe.Domain.Projects;

namespace Logaffe.Application.Ports;

/// <summary>
/// The tally rows an installation holds: what a flush adds to them, what reads
/// them back, and what the sweep takes out again.
/// </summary>
/// <remarks>
/// <para>
/// Through EF Core rather than around it, and for the reason
/// <see cref="ISamples"/> is: the log path earns a binary <c>COPY</c> at eleven
/// thousand entries a second (ADR 0003), and twenty projects writing a row an
/// hour each earn nothing of the sort. ADR 0003's rule read as written puts
/// this on the ordinary side of it.
/// </para>
/// <para>
/// <b>This is not a query surface.</b> Nothing here takes a filter, a cursor or
/// a page, and nothing above it is reachable from HTTP, from MCP or from the
/// interface. What an operator or an agent asks for a number is still the count
/// of <c>docs/querying.md</c>, over the entries themselves — that one takes
/// filters and this holds none.
/// </para>
/// </remarks>
public interface ITallies
{
    /// <summary>
    /// Adds what one flush counted, to the hours it counted it in, creating the
    /// rows that do not exist yet.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One call because it is one flush: the increments are the whole of what a
    /// minute produced, and half of them landing is a shape nothing downstream
    /// has a way to notice.
    /// </para>
    /// <para>
    /// <b>Adding, not setting.</b> A flush carries what arrived since the last
    /// one, so the row it meets is the same hour further along — writing the
    /// increment over it would leave every hour holding its final minute.
    /// </para>
    /// </remarks>
    Task AddAsync(IReadOnlyList<TallyIncrement> increments, CancellationToken cancellationToken);

    /// <summary>
    /// One project's hours from <paramref name="fromHour"/> up to but not
    /// including <paramref name="toHour"/>, oldest first.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The read both of this table's consumers take: the footprint a retention
    /// window implies (ADR 0048), and the conditions of ADR 0050 measuring an
    /// hour against a fortnight of the same hour. Neither reaches SQL of its
    /// own, which is what this port is for.
    /// </para>
    /// <para>
    /// <b>Hours nothing arrived in are absent rather than zero.</b> A project
    /// that was quiet has no row for the hour and a project that did not exist
    /// has none either, and the difference between the two is not in this table.
    /// Whoever reads it decides what a missing hour means, because the two
    /// consumers decide differently.
    /// </para>
    /// </remarks>
    Task<IReadOnlyList<Tally>> ReadAsync(
        Guid projectId,
        DateTimeOffset fromHour,
        DateTimeOffset toHour,
        CancellationToken cancellationToken);

    /// <summary>
    /// Every project identity the table still holds rows for.
    /// </summary>
    /// <remarks>
    /// Asked because deleting a project leaves its tally behind, exactly as it
    /// leaves its entries: there is no foreign key here either, for the reason
    /// ADR 0019 gives for not having one there. The sweep walks the projects,
    /// and a project that no longer exists is not on that walk.
    /// </remarks>
    Task<IReadOnlyList<Guid>> ProjectsWithTalliesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Removes every project's hours before <paramref name="hour"/>.
    /// </summary>
    /// <remarks>
    /// One statement across the whole table, unlike the entry and sample sweeps,
    /// which walk and portion. Both of those do it because a window is per
    /// project or the table is large; here the period is the same for every
    /// project (<see cref="Tallying.RetentionDays"/>) and a year of twenty
    /// projects is under two hundred thousand rows, of which one day's worth
    /// expires at a time.
    /// </remarks>
    Task RemoveHoursBeforeAsync(DateTimeOffset hour, CancellationToken cancellationToken);

    /// <summary>
    /// Removes everything one project's identity holds, which is what a deleted
    /// project left behind.
    /// </summary>
    Task RemoveProjectAsync(Guid projectId, CancellationToken cancellationToken);
}
