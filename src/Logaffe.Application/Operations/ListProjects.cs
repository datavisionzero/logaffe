using Logaffe.Application.Ports;
using Logaffe.Domain.Projects;

namespace Logaffe.Application.Operations;

/// <summary>
/// One project as the operator sees it on the list a session starts at.
/// </summary>
/// <param name="IngestTokens">
/// How many tokens the project can receive on: one ordinarily, two while it is
/// being rotated, and none for a project whose door is closed. That last case is
/// why the number is on the list at all — an operator should not have to open
/// each project to find the one nothing can deliver to.
/// </param>
public sealed record ListedProject(
    Guid Id,
    string Name,
    RetentionWindow Retention,
    DateTimeOffset CreatedAt,
    int IngestTokens);

/// <summary>
/// Every project the installation holds.
/// </summary>
/// <remarks>
/// <para>
/// This is where a session starts (<c>docs/ui.md</c>), and it is deliberately
/// not a dashboard: there is no count of entries beside a project, because that
/// is a query over the largest table in the database run for a number nobody
/// asked for.
/// </para>
/// <para>
/// Two reads, neither of them per row: the projects, and the token counts of
/// all of them at once. When that project last received an entry joins them
/// once the entry table exists — one indexed lookup per project, which is the
/// one fact <c>docs/ui.md</c> wants at a glance.
/// </para>
/// </remarks>
public sealed class ListProjects(IProjects projects, ITokens tokens)
{
    public async Task<IReadOnlyList<ListedProject>> ExecuteAsync(
        CancellationToken cancellationToken)
    {
        var held = await projects.ListAsync(cancellationToken);
        var counts = await tokens.CountIngestTokensAsync(cancellationToken);

        return [.. held.Select(project => new ListedProject(
            project.Id,
            project.Name,
            project.Retention,
            project.CreatedAt,
            counts.TryGetValue(project.Id, out var count) ? count : 0))];
    }
}
