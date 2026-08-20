using Logaffe.Application.Ports;
using Logaffe.Domain.Projects;

namespace Logaffe.Application.Operations;

/// <summary>
/// How a creation ended.
/// </summary>
public enum CreateProjectOutcome
{
    /// <summary>The project exists, and it is the only way one ever comes about.</summary>
    Created,

    /// <summary>
    /// Where it would be listed already holds a project by that name — inside
    /// the group it was given, or among the projects in no group.
    /// </summary>
    NameTaken,

    /// <summary>
    /// The group it was to be listed under is not there, which is ordinarily one
    /// removed from another browser while this screen was open.
    /// </summary>
    NoSuchGroup,
}

/// <summary>
/// The end of a creation, and the project it made when it succeeded.
/// </summary>
public sealed record CreationAttempt(CreateProjectOutcome Outcome, Project? Project);

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
/// <b>It may be given the group to list it under.</b> A group is chosen here
/// rather than only afterwards because creating a project and putting it where
/// it belongs is one errand, and making the operator open the new project's
/// settings to finish it is a second trip for something they already knew.
/// </para>
/// <para>
/// It is the operator's act and an administering agent's, and it is unreachable
/// from a reading token — a log entry asking the agent that read it to make a
/// project has to find nothing to call, which is a property of the tool list
/// that token is handed rather than a permission (ADR 0046).
/// </para>
/// </remarks>
public sealed class CreateProject(IProjects projects, IGroups groups, TimeProvider clock)
{
    /// <param name="groupId">
    /// The group to list it under, or <c>null</c> for none — which is what the
    /// creation offers by default, because most projects are in no group and an
    /// installation that holds none has nothing to choose.
    /// </param>
    /// <remarks>
    /// <para>
    /// Two projects called <c>api</c> is a trap for the operator reaching for
    /// one of them at three in the morning, and that is the whole reason the
    /// name is unique — there is no technical one. It has to be free where the
    /// project will be listed: inside the group it is given, or among the
    /// projects in no group.
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
    public async Task<CreationAttempt> ExecuteAsync(
        string name,
        RetentionWindow retention,
        Guid? groupId,
        CancellationToken cancellationToken)
    {
        // Asked before the insert so that naming a group another tab removed is
        // an answer rather than a foreign key violation surfacing as a failure
        // of the installation — the same reading as issuing into a project that
        // is gone.
        if (groupId is not null
            && await groups.FindAsync(groupId.Value, cancellationToken) is null)
        {
            return new CreationAttempt(CreateProjectOutcome.NoSuchGroup, null);
        }

        var taken = await projects.FindAsync(
            Project.NormalizeName(name), groupId, cancellationToken);
        if (taken is not null)
        {
            return new CreationAttempt(CreateProjectOutcome.NameTaken, null);
        }

        var project = Project.Create(name, retention, clock.GetUtcNow());
        project.MoveTo(groupId);
        await projects.AddAsync(project, cancellationToken);

        return new CreationAttempt(CreateProjectOutcome.Created, project);
    }
}
