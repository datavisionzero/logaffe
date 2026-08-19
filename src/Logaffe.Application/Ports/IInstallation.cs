using Logaffe.Domain.Operators;
using Logaffe.Domain.Projects;

namespace Logaffe.Application.Ports;

/// <summary>
/// What an installation knows about itself: when it last became claimable, the
/// hash of the claim secret it drew, and how long it keeps samples.
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
/// schema and what keeps Host Recovery writing to one store. The secret itself is
/// not here: what the row holds is a hash, and the value goes to the volume for
/// the operator to read (<see cref="IClaimSecretHandover"/>).
/// </para>
/// </remarks>
public interface IInstallation
{
    /// <summary>
    /// The guard as it stands, or <c>null</c> on an installation that has not had
    /// its first run written yet — which is a database somebody created by hand,
    /// since the start writes it.
    /// </summary>
    Task<ClaimGuard?> ReadClaimGuardAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Writes the first run, and answers the guard either way.
    /// </summary>
    /// <remarks>
    /// Called on every start and writing on exactly one of them: this is where
    /// "a restart does not extend it" lives, and where two containers coming up
    /// at once are decided by the row that is already there rather than by a
    /// check either of them could have run first.
    /// </remarks>
    Task<ClaimGuard> OpenClaimAsync(
        DateTimeOffset firstRunAt, CancellationToken cancellationToken);

    /// <summary>
    /// Opens the way in again, which is Host Recovery handing the installation
    /// back (ADR 0013): a fresh window, and no drawn secret until one is drawn.
    /// </summary>
    Task<ClaimGuard> ArmClaimAsync(DateTimeOffset at, CancellationToken cancellationToken);

    /// <summary>
    /// Writes back the secret the installation just drew for itself.
    /// </summary>
    /// <remarks>
    /// Separate from the two above because it happens on some starts and not
    /// others, and because it is the one thing here whose failure has to leave the
    /// installation unclaimable rather than claimable by a secret nobody was
    /// handed.
    /// </remarks>
    Task RecordClaimAsync(ClaimGuard guard, CancellationToken cancellationToken);

    /// <summary>
    /// How long samples are kept, which is one window for the installation
    /// rather than one per host.
    /// </summary>
    /// <remarks>
    /// It sits here rather than on a host because there is no reason to keep one
    /// machine's numbers longer than another's, and it is one field fewer on
    /// every host that is ever created (<c>docs/metrics.md</c>). An installation
    /// that has never been told answers
    /// <see cref="Domain.Hosts.Sampling.RetentionDaysByDefault"/>.
    /// </remarks>
    Task<RetentionWindow> ReadSampleRetentionAsync(CancellationToken cancellationToken);

    /// <summary>Writes back the window the operator just set.</summary>
    Task RecordSampleRetentionAsync(
        RetentionWindow window, CancellationToken cancellationToken);
}
