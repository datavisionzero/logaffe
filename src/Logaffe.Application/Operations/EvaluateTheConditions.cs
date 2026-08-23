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
/// with every switch off never walks the projects, and a muted project is not
/// evaluated at all — not evaluated and suppressed, but never asked, so a muted
/// project's conditions have no state to arm or latch.
/// </para>
/// <para>
/// <b>At most one alert per project, and the order here is what decides
/// which.</b> Going quiet comes first because it is the only one of the three
/// that is about nothing arriving at all. Failing comes before flooding because
/// it names what is wrong rather than how much of it there is: an operator told
/// both "twelve thousand entries" and "four thousand errors" about one hour
/// wanted the second sentence.
/// </para>
/// <para>
/// <b>What this does not collapse is one incident said twice across two
/// hours.</b> A retry storm can flood on the hour it starts and fail on the hour
/// after, because failing needs two consecutive hours and flooding needs one, so
/// the two alerts fall in different passes and no per-pass guard can see both.
/// That is accepted rather than worked around: they are different facts, the
/// second is the more specific one, and an hour apart is not the burst this
/// guard exists to stop (<c>docs/alerts.md</c>).
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
    CheckAProjectIsFailing failing,
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

        if (!switches.AnyProject)
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

            alert ??= switches.Failing
                ? await failing.ExecuteAsync(project, closedHour, cancellationToken)
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
