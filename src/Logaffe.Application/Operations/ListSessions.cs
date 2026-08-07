using Logaffe.Application.Ports;
using Logaffe.Domain.Operators;

namespace Logaffe.Application.Operations;

/// <summary>
/// The operator's signed-in browsers, as the list they act on.
/// </summary>
/// <remarks>
/// <para>
/// With no email anywhere in the product (ADR 0015) this list is the only way
/// the operator can ever notice a session that is not theirs, which makes it a
/// security surface rather than a convenience (<c>docs/sign-in.md</c>). What
/// makes it readable is <see cref="Session.LastSeenFrom"/> and
/// <see cref="Session.LastUsedAt"/>, and the second of those is accurate to
/// within five minutes and is not to be shown as though it were finer
/// (ADR 0033).
/// </para>
/// <para>
/// <b>An expired session is not in it.</b> A row nobody has swept yet admits
/// nothing — <see cref="Session.HasExpiredAt"/> is what refuses it — so showing
/// it would put a browser the operator cannot be signed in from on a list they
/// read for exactly that. The sweep removes the row; this decides what the
/// operator is asked to recognize.
/// </para>
/// <para>
/// <b>Which row is this browser is not answered here.</b> The act does not know
/// what it is being called by, and the adapter is holding the session that
/// admitted the request — so it is the adapter that marks it, and the operator
/// can tell before they end one.
/// </para>
/// </remarks>
public sealed class ListSessions(ISessions sessions, TimeProvider clock)
{
    public async Task<IReadOnlyList<Session>> ExecuteAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var held = await sessions.ListAsync(cancellationToken);

        return [.. held.Where(session => !session.HasExpiredAt(now))];
    }
}
