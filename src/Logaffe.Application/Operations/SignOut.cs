using Logaffe.Application.Ports;
using Logaffe.Domain.Operators;

namespace Logaffe.Application.Operations;

/// <summary>
/// Ends the session the operator is signing out of.
/// </summary>
/// <remarks>
/// <para>
/// It removes the row, exactly as revoking a token does. There is nothing to
/// mark: the session list is what the operator acts on, and one they ended has
/// to be gone from it rather than greyed out (<c>docs/sign-in.md</c>).
/// </para>
/// <para>
/// It is an act of its own rather than a call the endpoint makes into the store,
/// because signing out is one of the ways a session ends and the others —
/// revoked from the list, ended by a password change, swept for going untouched,
/// taken with the account by Host Recovery — will each want to say so in their
/// own place.
/// </para>
/// </remarks>
public sealed class SignOut(ISessions sessions)
{
    public Task ExecuteAsync(Session session, CancellationToken cancellationToken) =>
        sessions.RemoveAsync(session, cancellationToken);
}
