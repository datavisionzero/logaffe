using Logaffe.Application.Ports;
using Logaffe.Domain.Alerts;
using Logaffe.Domain.Hosts;

namespace Logaffe.Application.Operations;

/// <summary>
/// The three switches, read and written.
/// </summary>
/// <remarks>
/// <para>
/// All three at once rather than one route per condition, because they are one
/// setting with three parts: what an operator does here is decide what this
/// installation will say something about, and a screen that saved them
/// separately would have three ways to be half-applied.
/// </para>
/// <para>
/// <b>There is nothing else to change.</b> No threshold accompanies a switch, no
/// per-project variation of one, and no schedule — the conditions derive what
/// they compare against from the installation's own recent history, which is the
/// whole case for a closed set (ADR 0050). Switching one on while there is no
/// notifier is allowed and is a real state: the alert costs one line in the
/// installation's own log, and the screen carrying the switch says so.
/// </para>
/// </remarks>
public sealed class ChangeTheAlertSwitches(IInstallation installation)
{
    /// <summary>
    /// The switches as they stand, which is
    /// <see cref="AlertSwitches.AllOff"/> until an operator says otherwise.
    /// </summary>
    public Task<AlertSwitches> ReadAsync(CancellationToken cancellationToken) =>
        installation.ReadAlertSwitchesAsync(cancellationToken);

    public Task ExecuteAsync(AlertSwitches switches, CancellationToken cancellationToken) =>
        installation.RecordAlertSwitchesAsync(switches, cancellationToken);
}

/// <summary>How naming the machine this installation sits on ended.</summary>
public enum NameTheInstallationHostOutcome
{
    /// <summary>The installation names a machine and a mount, or names neither.</summary>
    Named,

    /// <summary>
    /// There is no such host, which is ordinarily one deleted from another
    /// browser while this screen was open.
    /// </summary>
    NoSuchHost,

    /// <summary>
    /// The mount is not a mount path. The screen picks from what the host
    /// reports rather than taking one typed, so this is the backstop rather than
    /// the ordinary refusal.
    /// </summary>
    NotAMount,
}

/// <summary>
/// Saying which machine logaffe itself runs on, and which of that machine's
/// filesystems holds the database.
/// </summary>
/// <remarks>
/// <para>
/// It is a fact about the installation rather than about the machine, so it sits
/// on the installation's own row and the host it names is an ordinary host
/// (<c>docs/metrics.md</c>). It exists so that two things can be read off
/// numbers that already exist: what the disk has left beside a retention window
/// (ADR 0048), and the condition that says the store is filling up (ADR 0050).
/// </para>
/// <para>
/// <b>The pair goes together.</b> A mount without a machine is a string and a
/// machine without a mount does not say which of its filesystems the database is
/// on, so naming takes both and clearing takes both.
/// </para>
/// <para>
/// <b>A mount the host is not currently reporting is accepted.</b> The screen
/// offers what the newest sample holds, but a machine that is switched off
/// reports nothing at all, and refusing here would mean an operator could not
/// name a mount while the collector was down — and could not correct one
/// afterwards either. What a named mount that never arrives costs is a condition
/// that says it cannot see (<see cref="Blindness.MountAbsent"/>), which is
/// legible where the switch is.
/// </para>
/// </remarks>
public sealed class NameTheInstallationHost(IInstallation installation, IHosts hosts)
{
    /// <summary>
    /// The machine and mount as they stand, or <c>null</c> on an installation
    /// that names none — which is every installation until the operator decides
    /// they want the disk read.
    /// </summary>
    public Task<InstallationHost?> ReadAsync(CancellationToken cancellationToken) =>
        installation.ReadHostAsync(cancellationToken);

    /// <param name="hostId">
    /// The machine, or <c>null</c> to name none — which takes the mount with it.
    /// </param>
    /// <param name="mount">
    /// The filesystem on it that holds the database, read past when
    /// <paramref name="hostId"/> is <c>null</c>.
    /// </param>
    public async Task<NameTheInstallationHostOutcome> ExecuteAsync(
        Guid? hostId, string? mount, CancellationToken cancellationToken)
    {
        if (hostId is null)
        {
            await installation.RecordHostAsync(null, cancellationToken);

            return NameTheInstallationHostOutcome.Named;
        }

        if (!MountPath.TryCreate(mount, out var path))
        {
            return NameTheInstallationHostOutcome.NotAMount;
        }

        // Asked before the write so that naming a host another tab removed is an
        // answer rather than a foreign key violation surfacing as a failure of
        // the installation.
        if (await hosts.FindAsync(hostId.Value, cancellationToken) is null)
        {
            return NameTheInstallationHostOutcome.NoSuchHost;
        }

        await installation.RecordHostAsync(
            new InstallationHost(hostId.Value, path), cancellationToken);

        return NameTheInstallationHostOutcome.Named;
    }
}
