using Logaffe.Application.Ports;
using Logaffe.Domain.Projects;

namespace Logaffe.Application.Operations;

/// <summary>
/// The operator bringing a project into existence, which is the only way one
/// ever comes about.
/// </summary>
/// <remarks>
/// <para>
/// There is no implicit creation on first delivery: a token that names nothing
/// admits nothing, and an installation's project list is exactly what the
/// operator put there (<c>docs/projects.md</c>). The first one usually comes
/// from the first-run guide after the claim.
/// </para>
/// <para>
/// It creates the project and nothing else. A project with no token receives
/// nothing until one is issued, and issuing is its own act — the same one the
/// operator reaches for when they rotate.
/// </para>
/// <para>
/// It is an operator act and is unreachable over MCP, which is a property of
/// the interface rather than a permission: a log entry asking an agent to make
/// a project has to find nothing to call (ADR 0018).
/// </para>
/// </remarks>
public sealed class CreateProject(IProjects projects, TimeProvider clock)
{
    /// <summary>
    /// The project, or <c>null</c> when the installation already holds one by
    /// that name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two projects called <c>api</c> is a trap for the operator reaching for
    /// one of them at three in the morning, and that is the whole reason the
    /// name is unique — there is no technical one.
    /// </para>
    /// <para>
    /// Two creations racing each other both pass this check and the second is
    /// refused by the unique index. That is one operator racing themselves in
    /// two browser tabs — there is exactly one account (ADR 0015) — and the
    /// database, not this, is what actually holds the rule.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> is not a name — it is blank, or longer than
    /// <see cref="Project.NameMaxLength"/>.
    /// </exception>
    public async Task<Project?> ExecuteAsync(
        string name, RetentionWindow retention, CancellationToken cancellationToken)
    {
        var taken = await projects.FindAsync(
            Project.NormalizeName(name), cancellationToken);
        if (taken is not null)
        {
            return null;
        }

        var project = Project.Create(name, retention, clock.GetUtcNow());
        await projects.AddAsync(project, cancellationToken);

        return project;
    }
}
