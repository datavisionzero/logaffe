using Logaffe.Application.Ports;

namespace Logaffe.Application.Operations;

/// <summary>
/// Ends one session from the operator's list.
/// </summary>
/// <remarks>
/// <para>
/// It removes the row, exactly as <see cref="SignOut"/> does and as revoking a
/// token does: the list is what the operator acts on, and one they ended has to
/// be gone from it rather than greyed out (<c>docs/sign-in.md</c>). The session
/// authentication reads the row on every request and holds no cache in front of
/// it, which is what makes this immediate.
/// </para>
/// <para>
/// <b>The row is found by reading the list.</b> There is no lookup by id in the
/// store and there is no reason to add one: an account holds a handful of these,
/// authenticating already reads all of them, and a second way in would be a
/// second thing to keep honest.
/// </para>
/// <para>
/// <b>Ending the session making the request is allowed</b> and is a sign-out by
/// another name. Refusing it would be a rule the operator has to learn in order
/// to be protected from something that costs them one sign-in — but the adapter
/// has to clear the cookie afterwards, or the browser keeps presenting a secret
/// whose row is gone.
/// </para>
/// </remarks>
public sealed class RevokeSession(ISessions sessions)
{
    /// <summary>
    /// Whether there was a session of that id to end. <c>false</c> is a second
    /// click or another tab, not a failure.
    /// </summary>
    public async Task<bool> ExecuteAsync(Guid id, CancellationToken cancellationToken)
    {
        var held = await sessions.ListAsync(cancellationToken);
        var session = held.FirstOrDefault(session => session.Id == id);

        if (session is null)
        {
            return false;
        }

        await sessions.RemoveAsync(session, cancellationToken);

        return true;
    }
}
