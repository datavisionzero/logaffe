using Logaffe.Domain.Operators;

namespace Logaffe.Application.Ports;

/// <summary>
/// Where a drawn claim secret is put so that the person installing can read it
/// and hand it to whoever is going to claim.
/// </summary>
/// <remarks>
/// <para>
/// A file on the host volume, beside the key (ADR 0022), readable by its owner
/// alone. It is a delivery copy and not a store: what says whether a presented
/// secret is the right one is the hash in the database (ADR 0034), and this is
/// the only form the secret itself ever takes.
/// </para>
/// <para>
/// It exists to be read once. The claim removes it, because what is left
/// otherwise is a credential for a door that no longer opens.
/// </para>
/// </remarks>
public interface IClaimSecretHandover
{
    /// <summary>Where the file is, for the log line that names it.</summary>
    string Path { get; }

    /// <summary>
    /// Writes the secret out, replacing whatever was there — Host Recovery draws
    /// a fresh one and the old one is void the moment it does.
    /// </summary>
    Task WriteAsync(ClaimSecret secret, CancellationToken cancellationToken);

    /// <summary>
    /// Removes the file, which the claim does. It is not an error for there to be
    /// nothing to remove: an installation whose secret came from configuration
    /// never wrote one.
    /// </summary>
    void Remove();
}
