using Logaffe.Domain.Identities;

namespace Logaffe.Application.Ports;

/// <summary>
/// The one-time secrets an installation has outstanding (ADR 0053).
/// </summary>
/// <remarks>
/// <para>
/// A secret is found by its hash, which is what the presented link produces —
/// there is no identifier beside it, because the value carries all of its own
/// entropy and names its row by being it.
/// </para>
/// <para>
/// <b>Issuing spends whatever was live of the same purpose, in one
/// transaction.</b> Two live links of one purpose are two chances for the older
/// one to be found in a mailbox later, and doing it in two statements would
/// leave a window in which there are.
/// </para>
/// </remarks>
public interface IOneTimeSecrets
{
    /// <summary>
    /// The secret behind a presented hash, whatever state it is in. A spent or
    /// expired one comes back too: refusing it is the caller's, so that every way
    /// of a link not working costs the same.
    /// </summary>
    Task<OneTimeSecret?> FindAsync(byte[] hash, CancellationToken cancellationToken);

    /// <summary>
    /// Writes a fresh one and spends whatever that user had live of the same
    /// purpose, together.
    /// </summary>
    Task IssueAsync(OneTimeSecret secret, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>
    /// Whether a live change of address is already moving somebody to this
    /// address. It is the other half of the reservation: the users' own unique
    /// index catches an address that is held, and this catches one that is
    /// spoken for.
    /// </summary>
    Task<bool> IsPendingAsync(string normalizedEmail, CancellationToken cancellationToken);

    /// <summary>Writes back the secret just spent.</summary>
    Task RecordAsync(OneTimeSecret secret, CancellationToken cancellationToken);
}
