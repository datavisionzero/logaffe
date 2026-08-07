using Logaffe.Application.Ports;
using Logaffe.Domain.Operators;

namespace Logaffe.Application.Operations;

/// <summary>
/// A session that admitted a request, and whether this use moved its deadline.
/// </summary>
/// <param name="DeadlineMoved">
/// Whether the sliding deadline was actually written forward, which is what the
/// adapter needs in order to keep the cookie's own expiry in step with the row's
/// without setting one on every response.
/// </param>
public sealed record AdmittedSession(Session Session, bool DeadlineMoved);

/// <summary>
/// What a presented session secret admits: the operator, or nothing.
/// </summary>
/// <remarks>
/// <para>
/// This is the door every operator surface stands behind, and the counterpart of
/// <see cref="AuthenticateToken"/> for the credential a person carries. The
/// shape is deliberately the same — refuse what is not a secret at all before
/// the database is asked anything, compare in constant time, record the use
/// coarsely, and say nothing about which of the possible refusals it was.
/// </para>
/// <para>
/// What differs is the lookup, and it differs for a reason ADR 0031 states from
/// the other side: a token names its own row because an installation holds
/// hundreds of them and they sit on the ingest path, while one account holds a
/// handful of sessions read by one human. So there is no identifier, and the
/// presented secret is compared against every session in turn.
/// </para>
/// </remarks>
public sealed class AuthenticateSession(ISessions sessions, TimeProvider clock)
{
    /// <summary>
    /// How stale a session's last use may be before another use writes it again.
    /// </summary>
    /// <remarks>
    /// The same five minutes a token gets, for the reason ADR 0033 gives: this
    /// row exists to be read by one human, occasionally, and the tail of a log
    /// view asking every few seconds must not be an <c>UPDATE</c> every few
    /// seconds. It follows that <see cref="Session.LastSeenFrom"/> is accurate
    /// to within five minutes as well, and is not to be shown as though it were
    /// finer.
    /// </remarks>
    public static readonly TimeSpan UseWriteInterval = TimeSpan.FromMinutes(5);

    public async Task<AdmittedSession?> ExecuteAsync(
        string? presented, string? seenFrom, CancellationToken cancellationToken)
    {
        // The wrong length, or a character outside base64url, is not a session
        // secret and never was one. It is refused here, and the table is not
        // read for it.
        if (!SessionSecret.TryParse(presented, out var secret))
        {
            return null;
        }

        var held = await sessions.ListAsync(cancellationToken);

        // Every session is compared and the loop does not stop at the one that
        // matched, for the same reason the backup codes are read that way: an
        // early return says where in the list the row sat.
        Session? matched = null;
        foreach (var session in held)
        {
            if (session.Matches(secret))
            {
                matched ??= session;
            }
        }

        if (matched is null)
        {
            return null;
        }

        var now = clock.GetUtcNow();

        // An expired session matches exactly as a live one does — the domain
        // says so on purpose — so refusing it is here. The row is left where it
        // is: removing the ones nobody touched is housekeeping and has its own
        // sweep, and a cookie the operator abandoned is not an event.
        if (matched.HasExpiredAt(now))
        {
            return null;
        }

        var moved = now - matched.LastUsedAt >= UseWriteInterval;
        if (moved)
        {
            matched.WasUsedAt(now, seenFrom);
            await sessions.RecordUseAsync(matched, cancellationToken);
        }

        return new AdmittedSession(matched, moved);
    }
}
