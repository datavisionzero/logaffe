using Logaffe.Application.Ports;
using Logaffe.Domain.Operators;

namespace Logaffe.Application.Operations;

/// <summary>
/// Removes the sessions that went untouched past their sliding deadline.
/// </summary>
/// <remarks>
/// <para>
/// It admits nothing either way: <see cref="Session.HasExpiredAt"/> is what
/// refuses an expired session, and <see cref="ListSessions"/> is what keeps it
/// off the operator's list. This is housekeeping — it keeps the table from
/// filling with rows that cannot act, on an installation where a browser is
/// opened once a quarter.
/// </para>
/// <para>
/// It is an act rather than a call the background service makes into the store,
/// for the reason <see cref="SignOut"/> gives: every way a session ends says so
/// in its own place, and this one is going to be read by whoever asks why a row
/// is gone that nobody signed out of.
/// </para>
/// </remarks>
public sealed class RemoveExpiredSessions(ISessions sessions, TimeProvider clock)
{
    public Task ExecuteAsync(CancellationToken cancellationToken) =>
        sessions.RemoveExpiredAsync(clock.GetUtcNow(), cancellationToken);
}
