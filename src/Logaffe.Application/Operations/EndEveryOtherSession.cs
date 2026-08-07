using Logaffe.Application.Ports;
using Logaffe.Domain.Operators;

namespace Logaffe.Application.Operations;

/// <summary>
/// Ends every session but the one asking, which is the operator's answer to a
/// row in the list they do not recognize.
/// </summary>
/// <remarks>
/// <para>
/// It is the whole point of the list being a security surface: somebody who
/// finds a session that is not theirs should not have to end rows one at a time
/// while the browser holding one of them is still acting.
/// </para>
/// <para>
/// <b>Every other, never every one.</b> The browser doing it stays signed in —
/// here, on a password change and on a re-enrolled second factor alike
/// (<c>docs/sign-in.md</c>), because signing the operator out of the screen they
/// just used to secure the installation teaches them not to use it.
/// </para>
/// </remarks>
public sealed class EndEveryOtherSession(ISessions sessions)
{
    public Task ExecuteAsync(Session kept, CancellationToken cancellationToken) =>
        sessions.RemoveEveryOtherAsync(kept, cancellationToken);
}
