using Logaffe.Application.Ports;
using Logaffe.Domain.Identities;

namespace Logaffe.Application.Operations;

/// <summary>
/// One account as an administrator sees it in the list.
/// </summary>
/// <param name="HasSecondFactor">
/// Whether this account asks for a code at the next sign-in. An administrator
/// sees it and cannot require it (<c>docs/sign-in.md</c>): somebody who cannot
/// see it cannot have the conversation, and a forced enrolment is the one most
/// likely to be done badly.
/// </param>
/// <param name="Projects">
/// How many projects they reach. The names are on the project's own assignment
/// screen; what belongs in a list of people is the count
/// ([ADR 0055](../../../docs/adr/0055-project-access-is-one-filter.md)).
/// </param>
public sealed record ListedUser(
    Guid Id,
    string Name,
    string Email,
    UserState State,
    bool Administrator,
    bool HasSecondFactor,
    int Projects,
    DateTimeOffset CreatedAt);

/// <summary>
/// The people on this installation, as the list an administrator works.
/// </summary>
public sealed class ListUsers(IIdentities identities, IProjectAccess access)
{
    public async Task<IReadOnlyList<ListedUser>> ExecuteAsync(
        CancellationToken cancellationToken)
    {
        var users = await identities.ListUsersAsync(cancellationToken);
        var listed = new List<ListedUser>(users.Count);

        foreach (var user in users)
        {
            var held = await access.ListForUserAsync(user.Id, cancellationToken);

            listed.Add(new ListedUser(
                user.Id,
                user.Name,
                user.Email,
                user.State,
                user.Administrator,
                user.HasSecondFactor,
                held.Count,
                user.CreatedAt));
        }

        return listed;
    }
}

/// <summary>
/// How an act on somebody else's account ended.
/// </summary>
public enum UserActOutcome
{
    Done,

    /// <summary>No user of that id, or an identity that is not a user.</summary>
    NoSuchUser,

    /// <summary>
    /// It would have left the installation with no active administrator. There
    /// is always at least one, and the last one can be neither deactivated nor
    /// stripped of the role
    /// ([ADR 0052](../../../docs/adr/0052-a-user-and-an-agent-are-one-identity.md)).
    /// </summary>
    TheLastAdministrator,
}

/// <summary>
/// Closing an account and opening it again (ADR 0052).
/// </summary>
/// <remarks>
/// <para>
/// <b>Deactivating is not deleting</b>, and there is no act anywhere that
/// deletes an identity: everything pointing at somebody — a project assignment,
/// a record of a change — keeps pointing at something. What deactivating does is
/// stop the account signing in, end every session it had, and silence its agents
/// — the last of which needs no act at all, because an agent's token resolves
/// its owner's state on every call.
/// </para>
/// <para>
/// <b>Reactivating restores what was not individually revoked.</b> Their project
/// assignments were never touched, their agents' tokens start admitting again,
/// and an account that was invited and never arrived goes back to invited rather
/// than becoming active without a password.
/// </para>
/// <para>
/// <b>There is always one active administrator.</b> Without that rule an
/// installation can be talked into a state whose only exit is Host Recovery, and
/// an administrator removing their own role by accident is the likeliest way in.
/// </para>
/// </remarks>
public sealed class ChangeAUser(IIdentities identities, ISessions sessions)
{
    public async Task<UserActOutcome> DeactivateAsync(
        Guid userId, CancellationToken cancellationToken)
    {
        var user = await identities.FindUserAsync(userId, cancellationToken);
        if (user is null)
        {
            return UserActOutcome.NoSuchUser;
        }

        if (await WouldLeaveNobodyAsync(user, losingTheRole: true, cancellationToken))
        {
            return UserActOutcome.TheLastAdministrator;
        }

        user.Deactivate();
        await identities.RecordAsync(user, cancellationToken);

        // Every session, immediately. The state is read on every request, so
        // this is housekeeping rather than what makes the deactivation take
        // effect — and it is done anyway, because a list of live sessions
        // belonging to a closed account is a list that lies.
        await sessions.RemoveEveryOfAsync(user.Id, cancellationToken);

        return UserActOutcome.Done;
    }

    public async Task<UserActOutcome> ReactivateAsync(
        Guid userId, CancellationToken cancellationToken)
    {
        var user = await identities.FindUserAsync(userId, cancellationToken);
        if (user is null)
        {
            return UserActOutcome.NoSuchUser;
        }

        user.Reactivate();
        await identities.RecordAsync(user, cancellationToken);

        return UserActOutcome.Done;
    }

    public async Task<UserActOutcome> ChangeRoleAsync(
        Guid userId, bool administrator, CancellationToken cancellationToken)
    {
        var user = await identities.FindUserAsync(userId, cancellationToken);
        if (user is null)
        {
            return UserActOutcome.NoSuchUser;
        }

        if (!administrator
            && await WouldLeaveNobodyAsync(user, losingTheRole: true, cancellationToken))
        {
            return UserActOutcome.TheLastAdministrator;
        }

        user.ChangeAdministratorRole(administrator);
        await identities.RecordAsync(user, cancellationToken);

        return UserActOutcome.Done;
    }

    /// <summary>
    /// Whether doing this to <paramref name="user"/> would leave the
    /// installation without an active administrator.
    /// </summary>
    /// <remarks>
    /// Counted rather than reasoned about: the count is what the rule is, and
    /// asking the database for it is what keeps the answer from being one this
    /// process worked out from a stale list.
    /// </remarks>
    private async Task<bool> WouldLeaveNobodyAsync(
        User user, bool losingTheRole, CancellationToken cancellationToken)
    {
        if (!losingTheRole || !user.Administrator || !user.IsActive)
        {
            return false;
        }

        return await identities.CountActiveAdministratorsAsync(cancellationToken) <= 1;
    }
}
