using Logaffe.Application.Ports;
using Logaffe.Domain.History;
using Logaffe.Domain.Identities;
using Logaffe.Domain.Projects;

namespace Logaffe.Application.Operations;

/// <summary>
/// What one request may see, resolved once from the identity behind it
/// (ADR 0055).
/// </summary>
/// <remarks>
/// <para>
/// Once per request and not once per read: a reach that were resolved twice
/// inside one request could answer differently in the middle of it, and a screen
/// assembled out of two answers is a screen that contradicts itself.
/// </para>
/// <para>
/// <b>An administrator's reach is their own and no larger.</b> The role is about
/// running the installation — inviting people, handing out assignments — and not
/// about looking into it. An administrator who needs to read a project assigns
/// it to themselves, which is an act that leaves a record, rather than holding a
/// capability that is invisible because it was never used.
/// </para>
/// </remarks>
public sealed class ResolveReach(IProjectAccess access)
{
    public Task<Reach> ExecuteAsync(User user, CancellationToken cancellationToken) =>
        access.ReachOfAsync(user.Id, cancellationToken);
}

/// <summary>
/// One person who reaches a project, as the list an administrator works.
/// </summary>
public sealed record AssignedUser(
    Guid UserId, string Name, string Email, Guid GrantedBy, DateTimeOffset GrantedAt);

/// <summary>
/// Who reaches one project, with the people named rather than identified
/// (ADR 0055).
/// </summary>
/// <remarks>
/// The project is looked up against the installation rather than against the
/// administrator's own reach, for the reason the assignment acts are: handing
/// out access is exactly the case where somebody works on a project they cannot
/// open.
/// </remarks>
public sealed class ListProjectAccess(
    IProjects projects, IIdentities identities, IProjectAccess access)
{
    /// <returns><c>null</c> when there is no such project.</returns>
    public async Task<IReadOnlyList<AssignedUser>?> ExecuteAsync(
        Guid projectId, CancellationToken cancellationToken)
    {
        if (await projects.FindAsync(Reach.TheInstallation, projectId, cancellationToken) is null)
        {
            return null;
        }

        var held = await access.ListForProjectAsync(projectId, cancellationToken);
        var users = (await identities.ListUsersAsync(cancellationToken))
            .ToDictionary(user => user.Id);

        // A row whose user is gone cannot happen — the assignment cascades with
        // the identity — so one that is missing is a fault rather than a state,
        // and leaving it out is the reading that does not invent a name for it.
        return
        [
            .. held
                .Where(assignment => users.ContainsKey(assignment.UserId))
                .Select(assignment => new AssignedUser(
                    assignment.UserId,
                    users[assignment.UserId].Name,
                    users[assignment.UserId].Email,
                    assignment.GrantedBy,
                    assignment.GrantedAt)),
        ];
    }
}

/// <summary>
/// How an assignment ended.
/// </summary>
public enum AssignProjectOutcome
{
    /// <summary>The assignment is there — written now, or already.</summary>
    Assigned,

    /// <summary>Taken away, or it was not there to take.</summary>
    Withdrawn,

    /// <summary>No project of that id, whoever is asking.</summary>
    NoSuchProject,

    /// <summary>No user of that id, or one that is not a user at all.</summary>
    NoSuchUser,
}

/// <summary>
/// An administrator putting a project within somebody's reach, or taking it back
/// (ADR 0055).
/// </summary>
/// <remarks>
/// <para>
/// It is the one act in the product that changes what another person can see,
/// and it is an administrator's alone. The project is looked up against the
/// installation rather than against the administrator's own reach: handing out
/// access is exactly the case where somebody works on a project they cannot open,
/// and requiring them to assign it to themselves first would make the role
/// self-granting by the back door.
/// </para>
/// <para>
/// <b>Taking one away takes effect on that person's next request.</b> The reach
/// is resolved per request and nothing caches it in front, so there is no
/// interval in which a withdrawn assignment still admits.
/// </para>
/// </remarks>
public sealed class AssignProject(
    IProjects projects,
    IIdentities identities,
    IProjectAccess access,
    RecordAChange record,
    TimeProvider clock)
{
    public async Task<AssignProjectOutcome> GrantAsync(
        Guid projectId, Guid userId, Guid grantedBy, CancellationToken cancellationToken)
    {
        var refusal = await NotThereAsync(projectId, userId, cancellationToken);
        if (refusal is not null)
        {
            return refusal.Value;
        }

        await access.GrantAsync(
            ProjectAccess.Grant(projectId, userId, grantedBy, clock.GetUtcNow()),
            cancellationToken);

        await RecordAsync(projectId, userId, Act.Granted, cancellationToken);

        return AssignProjectOutcome.Assigned;
    }

    public async Task<AssignProjectOutcome> WithdrawAsync(
        Guid projectId, Guid userId, CancellationToken cancellationToken)
    {
        var refusal = await NotThereAsync(projectId, userId, cancellationToken);
        if (refusal is not null)
        {
            return refusal.Value;
        }

        await access.RevokeAsync(projectId, userId, cancellationToken);

        await RecordAsync(projectId, userId, Act.Withdrawn, cancellationToken);

        return AssignProjectOutcome.Withdrawn;
    }

    /// <summary>
    /// One row naming both halves, because *who gave this person access* is a
    /// question about a pair and neither half alone answers it.
    /// </summary>
    private async Task RecordAsync(
        Guid projectId, Guid userId, Act act, CancellationToken cancellationToken)
    {
        var project = await projects.FindAsync(
            Reach.TheInstallation, projectId, cancellationToken);
        var user = await identities.FindUserAsync(userId, cancellationToken);

        await record.ExecuteAsync(
            Subject.ProjectAccess,
            projectId,
            project?.Name ?? "a project",
            act,
            cancellationToken,
            to: user?.Email);
    }

    /// <summary>
    /// Which of the two things named is not there, and <c>null</c> when both
    /// are.
    /// </summary>
    private async Task<AssignProjectOutcome?> NotThereAsync(
        Guid projectId, Guid userId, CancellationToken cancellationToken)
    {
        if (await projects.FindAsync(Reach.TheInstallation, projectId, cancellationToken) is null)
        {
            return AssignProjectOutcome.NoSuchProject;
        }

        return await identities.FindUserAsync(userId, cancellationToken) is null
            ? AssignProjectOutcome.NoSuchUser
            : null;
    }
}
