using Logaffe.Application.Ports;
using Logaffe.Domain.Identities;

namespace Logaffe.Application.Operations;

/// <summary>
/// One user's signed-in browsers, as the list they act on.
/// </summary>
/// <remarks>
/// <para>
/// This list is how somebody notices a session that is not theirs, which makes
/// it a security surface rather than a convenience (<c>docs/sign-in.md</c>).
/// <b>Nobody is ever shown anybody else's</b>, not even an administrator: it is
/// a record of where a person has been, and the role is about running the
/// installation rather than about looking into it (ADR 0055). What
/// makes it readable is <see cref="Session.LastSeenFrom"/> and
/// <see cref="Session.LastUsedAt"/>, and the second of those is accurate to
/// within five minutes and is not to be shown as though it were finer
/// (ADR 0033).
/// </para>
/// <para>
/// <b>An expired session is not in it.</b> A row nobody has swept yet admits
/// nothing — <see cref="Session.HasExpiredAt"/> is what refuses it — so showing
/// it would put a browser nobody can be signed in from on a list read for
/// exactly that. The sweep removes the row; this decides what a person is asked
/// to recognize.
/// </para>
/// <para>
/// <b>Which row is this browser is not answered here.</b> The act does not know
/// what it is being called by, and the adapter is holding the session that
/// admitted the request — so it is the adapter that marks it, and a person can
/// tell before they end one.
/// </para>
/// </remarks>
public sealed class ListSessions(ISessions sessions, TimeProvider clock)
{
    public async Task<IReadOnlyList<Session>> ExecuteAsync(
        Guid userId, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var held = await sessions.ListForAsync(userId, cancellationToken);

        return [.. held.Where(session => !session.HasExpiredAt(now))];
    }
}
