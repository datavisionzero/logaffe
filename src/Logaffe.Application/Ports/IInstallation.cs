using Logaffe.Domain.Operators;

namespace Logaffe.Application.Ports;

/// <summary>
/// What an installation knows about itself, which today is one fact: when it
/// last became claimable.
/// </summary>
/// <remarks>
/// <para>
/// Singular on purpose, unlike every other port here. There is one installation
/// and the row it holds is one row, so there is nothing to look up and nothing
/// to list — the methods take no identifier because there is none to take.
/// </para>
/// <para>
/// It is a table rather than a file on the host volume beside the key
/// (ADR 0034), which is what makes "first run" mean the run that created the
/// schema and what keeps Host Recovery writing to one store.
/// </para>
/// </remarks>
public interface IInstallation
{
    /// <summary>
    /// The window as it stands, or <c>null</c> on an installation that has not
    /// had its first run written yet — which is a database somebody created by
    /// hand, since the start writes it.
    /// </summary>
    Task<ClaimWindow?> ReadClaimWindowAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Writes the first run, and answers the window either way.
    /// </summary>
    /// <remarks>
    /// Called on every start and writing on exactly one of them: this is where
    /// "a restart does not extend it" lives, and where two containers coming up
    /// at once are decided by the row that is already there rather than by a
    /// check either of them could have run first.
    /// </remarks>
    Task<ClaimWindow> OpenClaimWindowAsync(
        DateTimeOffset firstRunAt, CancellationToken cancellationToken);

    /// <summary>
    /// Arms a fresh window, which is Host Recovery handing the installation back
    /// (ADR 0013).
    /// </summary>
    Task<ClaimWindow> ArmClaimWindowAsync(
        DateTimeOffset at, CancellationToken cancellationToken);
}
