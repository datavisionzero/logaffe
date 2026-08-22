using Logaffe.Application.Ports;
using Logaffe.Domain.Alerts;
using Logaffe.Domain.Hosts;

namespace Logaffe.Application.Operations;

/// <summary>
/// Whether the filesystem the installation's database sits on has crossed a
/// threshold it had not crossed before.
/// </summary>
/// <remarks>
/// <para>
/// The one condition that is not about a project, and the one that ends in a
/// database that stops accepting writes. It reads the newest filesystem reading
/// of the host the installation is named onto and compares it against two
/// numbers the product fixed (ADR 0050); nothing new is collected, nothing is
/// asked of Postgres, and no entry is read.
/// </para>
/// <para>
/// <b>Each threshold notifies once and arms again when the figure falls back
/// below it.</b> Crossing 85 sends one notification; the disk continuing to fill
/// sends nothing more until it reaches 95, which sends the second at once rather
/// than waiting out the silence — a disk that has gone from one to the other in
/// an afternoon is the alarm this condition exists for.
/// </para>
/// <para>
/// <b>It is not evaluated at all, and says so, when it cannot be.</b> An
/// operator who thinks a disk is being watched when it is not is worse off than
/// one who was never offered the switch, so <see cref="ReadAsync"/> answers what
/// is in the way and the screen carrying the switch says it.
/// </para>
/// </remarks>
public sealed class CheckTheStoreIsFillingUp(
    IInstallation installation,
    IHosts hosts,
    ISampleReader samples,
    IConditionStates states,
    TimeProvider clock)
{
    /// <summary>
    /// The alert this hour warrants, or <c>null</c> — which is a disk below both
    /// thresholds, a threshold already said, and a condition that cannot see.
    /// </summary>
    public async Task<Alert?> ExecuteAsync(CancellationToken cancellationToken)
    {
        var fullness = await ReadAsync(cancellationToken);
        if (fullness.Blindness is not Blindness.None)
        {
            return null;
        }

        var crossed = fullness.Crossed;

        if (!await Firing.DecideAsync(
            states,
            fullness.HostId,
            AlertCondition.FillingUp,
            crossed,
            clock.GetUtcNow(),
            cancellationToken))
        {
            return null;
        }

        return new Alert.StoreFillingUp(
            fullness.HostId, fullness.HostName, fullness.Percent, crossed);
    }

    /// <summary>
    /// How full the named mount is, or what stands between this installation and
    /// knowing — which is the read the settings screen takes to say whether the
    /// switch is doing anything.
    /// </summary>
    /// <remarks>
    /// <b>A reading older than <see cref="Alerting.Reporting"/> is no
    /// reading.</b> A machine reports its filesystems every minute, so one that
    /// last spoke an hour ago is a machine whose disk this installation cannot
    /// vouch for.
    /// </remarks>
    public async Task<StoreFullness> ReadAsync(CancellationToken cancellationToken)
    {
        var named = await installation.ReadHostAsync(cancellationToken);
        if (named is null)
        {
            return StoreFullness.Blind(Blindness.NoHostNamed);
        }

        var host = await hosts.FindAsync(named.HostId, cancellationToken);
        if (host is null)
        {
            // The machine was deleted and the set-null on the settings row has
            // not been read back yet, or is about to be. There is no host to
            // name in an alert, so there is nothing to say.
            return StoreFullness.Blind(Blindness.NoHostNamed);
        }

        var reports = await samples.NewestReportsAsync([named.HostId], cancellationToken);
        var report = reports.FirstOrDefault(r => r.HostId == named.HostId);

        if (report is null || clock.GetUtcNow() - report.ReceiptTime > Alerting.Reporting)
        {
            return StoreFullness.Blind(Blindness.NotReporting);
        }

        var reading = report.Filesystems.FirstOrDefault(f => f.MountPath == named.Mount);

        return reading is null
            ? StoreFullness.Blind(Blindness.MountAbsent)
            : StoreFullness.Of(host.Id, host.Name, reading.Used, reading.Total);
    }
}
