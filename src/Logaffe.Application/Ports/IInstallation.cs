using Logaffe.Domain.Alerts;
using Logaffe.Domain.Hosts;
using Logaffe.Domain.Operators;
using Logaffe.Domain.Projects;

namespace Logaffe.Application.Ports;

/// <summary>
/// What an installation knows about itself: when it last became claimable, the
/// hash of the claim secret it drew, how long it keeps samples, the machine it
/// sits on, which conditions it has been switched on, and where what they decide
/// is sent.
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

    /// <summary>
    /// The machine this installation runs on and the mount holding its database,
    /// or <c>null</c> when it names none — which is every installation until the
    /// operator says otherwise.
    /// </summary>
    /// <remarks>
    /// It is on the installation's own row rather than a flag on a host, because
    /// it is a fact about logaffe and not about the machine: the host named here
    /// is an ordinary host, created and named the way any other is
    /// (<c>docs/metrics.md</c>).
    /// </remarks>
    Task<InstallationHost?> ReadHostAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Names the machine and the mount, or clears both by being given
    /// <c>null</c>.
    /// </summary>
    /// <remarks>
    /// The pair goes together: a mount without a machine is a string, and a
    /// machine without a mount does not say which of its filesystems the
    /// database is on. Deleting the host clears it, which is the projects'
    /// behaviour and for the projects' reason.
    /// </remarks>
    Task RecordHostAsync(InstallationHost? host, CancellationToken cancellationToken);

    /// <summary>
    /// Which of the four conditions are switched on, which is none of them
    /// until the operator switches one on (ADR 0050).
    /// </summary>
    /// <remarks>
    /// Read before anything else on the hourly pass, and read here rather than
    /// per project: the switch is the installation's and the mute is the
    /// project's, so an installation with all three off evaluates nothing at all
    /// and never walks the projects.
    /// </remarks>
    Task<AlertSwitches> ReadAlertSwitchesAsync(CancellationToken cancellationToken);

    /// <summary>Writes back the switches as the operator left them.</summary>
    Task RecordAlertSwitchesAsync(
        AlertSwitches switches, CancellationToken cancellationToken);

    /// <summary>
    /// Where notifications go, or <c>null</c> on an installation that has not
    /// been given a notifier — which is every installation until the operator
    /// configures one, and any of them again after they clear it.
    /// </summary>
    /// <remarks>
    /// The access token comes back sealed, because that is what the row holds
    /// (ADR 0022). Opening it is the sending adapter's business and the
    /// operator's read-back, and neither is a thing this port does on the way
    /// past.
    /// </remarks>
    Task<Notifier?> ReadNotifierAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Writes the notifier down, or clears it by being given <c>null</c>.
    /// </summary>
    /// <remarks>
    /// The three parts go together the way the host and its mount do: a topic
    /// without a server is a word, and a token without either is a secret
    /// belonging to nothing. Clearing takes all three, including the sealed
    /// token — an installation with no notifier holds no credential for one.
    /// </remarks>
    Task RecordNotifierAsync(Notifier? notifier, CancellationToken cancellationToken);
}
