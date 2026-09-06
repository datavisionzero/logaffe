using Logaffe.Domain.Identities;

namespace Logaffe.Application.Ports;

/// <summary>
/// The sessions an installation holds, which are its users' signed-in browsers.
/// </summary>
/// <remarks>
/// <para>
/// There is no lookup by the presented secret. An installation of this size
/// holds a handful of these, so authenticating is the whole list and a
/// constant-time comparison against each — which is the same shape as a backup
/// code and deliberately not the shape of a token, whose table is looked up by an
/// identifier because it can hold hundreds and is on the ingest path
/// (ADR 0031).
/// </para>
/// <para>
/// Ending a session is removing its row. There is nothing to mark: the list is
/// what a person acts on, and a session they ended has to be gone from it rather
/// than greyed out.
/// </para>
/// </remarks>
public interface ISessions
{
    /// <summary>
    /// Every session on the installation, newest first. This is what
    /// authentication compares against, and it is the only place the whole table
    /// is read.
    /// </summary>
    Task<IReadOnlyList<Session>> ListAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The sessions of one user, newest first, which is what that user is shown
    /// (<c>docs/ui.md</c>). Nobody is ever shown anybody else's: the list is a
    /// record of where a person has been.
    /// </summary>
    Task<IReadOnlyList<Session>> ListForAsync(Guid userId, CancellationToken cancellationToken);

    Task AddAsync(Session session, CancellationToken cancellationToken);

    /// <summary>Ends one session: signed out, or revoked from the list.</summary>
    Task RemoveAsync(Session session, CancellationToken cancellationToken);

    /// <summary>
    /// Ends every session of this user but the one being kept — "end all
    /// others", and what a password change and a re-enrolled second factor both
    /// do (<c>docs/sign-in.md</c>). It never reaches anybody else's.
    /// </summary>
    Task RemoveEveryOtherAsync(Session kept, CancellationToken cancellationToken);

    /// <summary>
    /// Ends every session of one user, which is what deactivating them does
    /// (ADR 0052).
    /// </summary>
    Task RemoveEveryOfAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Removes the sessions that went past a deadline — idle or absolute. They
    /// admit nothing either way, since <see cref="Session.HasExpiredAt"/> is
    /// what refuses them, so this is housekeeping: it keeps a list somebody
    /// reads for anything unfamiliar from filling up with rows that cannot act.
    /// </summary>
    Task RemoveExpiredAsync(DateTimeOffset asOf, CancellationToken cancellationToken);

    /// <summary>
    /// Writes back the use just recorded on <paramref name="session"/>. How
    /// often that is worth doing is the caller's, and the answer is ADR 0033's.
    /// </summary>
    Task RecordUseAsync(Session session, CancellationToken cancellationToken);
}
