using Logaffe.Application.Ports;
using Logaffe.Domain.Alerts;

namespace Logaffe.Application.Operations;

/// <summary>
/// The hourly pass: the switches, the hour that has just closed, and at most one
/// thing said about each project.
/// </summary>
/// <remarks>
/// <para>
/// <b>It reads no entry.</b> Every condition runs on the tally and the samples,
/// there is no path from here to <c>log_entry</c>, and that is what makes the
/// rule about what a notification carries a property of what this code can reach
/// rather than a rule it has to remember (ADR 0049). It is also what keeps the
/// pass cheap: a few hundred small rows on the hour, whatever the entry table
/// has grown to.
/// </para>
/// <para>
/// <b>The switches come first and the mute comes second.</b> An installation
/// with all three off never walks the projects, and a muted project is not
/// evaluated at all — not evaluated and suppressed, but never asked, so a muted
/// project's conditions have no state to arm or latch.
/// </para>
/// <para>
/// <b>At most one alert per project.</b> The two project conditions are near
/// enough opposites that both holding is hard to arrange — an hour with nothing
/// in it is not an hour with too much in it — but the guard is worth having on
/// its own account: two notifications about one project in one minute is the
/// shape of a thing an operator learns to swipe away.
/// </para>
/// <para>
/// It is a duty on the retention pass rather than a timer of its own, and it
/// goes first on it (<c>RetentionService</c>).
/// </para>
/// </remarks>
public sealed class EvaluateTheConditions(
    IInstallation installation,
    IProjects projects,
    CheckTheStoreIsFillingUp fillingUp,
    CheckAProjectHasGoneQuiet goneQuiet,
    CheckAProjectIsFlooding flooding,
    IAlertNotifier notifier,
    TimeProvider clock)
{
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var switches = await installation.ReadAlertSwitchesAsync(cancellationToken);
        if (!switches.Any)
        {
            return;
        }

        if (switches.FillingUp)
        {
            await SendAsync(await fillingUp.ExecuteAsync(cancellationToken), cancellationToken);
        }

        if (!switches.GoneQuiet && !switches.Flooding)
        {
            return;
        }

        var closedHour = Alerting.ClosedHourAt(clock.GetUtcNow());

        foreach (var project in await projects.ListAsync(cancellationToken))
        {
            if (project.Muted)
            {
                continue;
            }

            var alert = switches.GoneQuiet
                ? await goneQuiet.ExecuteAsync(project, closedHour, cancellationToken)
                : null;

            alert ??= switches.Flooding
                ? await flooding.ExecuteAsync(project, closedHour, cancellationToken)
                : null;

            await SendAsync(alert, cancellationToken);
        }
    }

    /// <remarks>
    /// The state was written before this was reached, so a notifier that fails
    /// costs the alert rather than a repeat of it on the next pass — which is
    /// the trade ADR 0050 makes deliberately: a queue of undelivered alerts
    /// arriving together an hour later is a burst of notifications about things
    /// that are no longer true.
    /// </remarks>
    private async Task SendAsync(Alert? alert, CancellationToken cancellationToken)
    {
        if (alert is not null)
        {
            await notifier.SendAsync(alert, cancellationToken);
        }
    }
}
