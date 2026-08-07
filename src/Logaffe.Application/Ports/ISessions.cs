using Logaffe.Domain.Operators;

namespace Logaffe.Application.Ports;

/// <summary>
/// The sessions an installation holds, which are the operator's signed-in
/// browsers.
/// </summary>
/// <remarks>
/// <para>
/// There is no lookup by the presented secret. One account holds a handful of
/// these, so authenticating is the whole list and a constant-time comparison
/// against each — which is the same shape as a backup code and deliberately not
/// the shape of a token, whose table is looked up by an identifier because it
/// can hold hundreds and is on the ingest path (ADR 0031).
/// </para>
/// <para>
/// Ending a session is removing its row. There is nothing to mark: the list is
/// what the operator acts on, and a session they ended has to be gone from it
/// rather than greyed out.
/// </para>
/// </remarks>
public interface ISessions
{
    /// <summary>
    /// Every session, newest first, which is both what authentication compares
    /// against and what the operator is shown (<c>docs/ui.md</c>).
    /// </summary>
    Task<IReadOnlyList<Session>> ListAsync(CancellationToken cancellationToken);

    Task AddAsync(Session session, CancellationToken cancellationToken);

    /// <summary>Ends one session: signed out, or revoked from the list.</summary>
    Task RemoveAsync(Session session, CancellationToken cancellationToken);

    /// <summary>
    /// Ends every session but the one being kept — "end all others", and what a
    /// password change and a re-enrolled second factor both do
    /// (<c>docs/sign-in.md</c>).
    /// </summary>
    Task RemoveEveryOtherAsync(Session kept, CancellationToken cancellationToken);

    /// <summary>
    /// Removes the sessions that went untouched past their sliding deadline.
    /// They admit nothing either way — <see cref="Session.HasExpiredAt"/> is
    /// what refuses them — so this is housekeeping, and it keeps a list the
    /// operator reads for anything unfamiliar from filling up with rows that
    /// cannot act.
    /// </summary>
    Task RemoveExpiredAsync(DateTimeOffset asOf, CancellationToken cancellationToken);

    /// <summary>
    /// Writes back the use just recorded on <paramref name="session"/>. How
    /// often that is worth doing is the caller's, and the answer is ADR 0033's.
    /// </summary>
    Task RecordUseAsync(Session session, CancellationToken cancellationToken);
}
