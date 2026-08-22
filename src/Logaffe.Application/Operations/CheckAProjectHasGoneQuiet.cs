using Logaffe.Application.Ports;
using Logaffe.Domain.Alerts;
using Logaffe.Domain.Projects;

namespace Logaffe.Application.Operations;

/// <summary>
/// Whether a project has received nothing for longer than its own fortnight says
/// is ordinary.
/// </summary>
/// <remarks>
/// <para>
/// This is the condition a self-hoster gets the most out of: a service that died
/// is usually discovered by noticing that nothing has been logged, which is a
/// thing nobody notices. It runs on the tally alone — the hours a project has a
/// row for, and nothing about what was in them.
/// </para>
/// <para>
/// <b>What it tolerates is the project's own behaviour</b>, three times over
/// (<see cref="Quiet"/>): a project that delivers every hour is noticed on its
/// second silent hour, and one that is idle every night from one until six
/// tolerates fifteen and is not woken for its nights. Nothing here is a number
/// the operator typed, which is what makes the closed set defensible.
/// </para>
/// <para>
/// <b>A project without a fortnight behind it has no alarm</b>, however it
/// behaves. A project created this morning has no normal to have departed from,
/// and neither has an installation restored from a backup this morning — this is
/// the first two weeks of both, and it is the guard that keeps the first
/// fortnight of every project quiet.
/// </para>
/// </remarks>
public sealed class CheckAProjectHasGoneQuiet(
    ITallies tallies, IConditionStates states, TimeProvider clock)
{
    /// <summary>
    /// The alert this closed hour warrants for <paramref name="project"/>, or
    /// <c>null</c>.
    /// </summary>
    public async Task<Alert?> ExecuteAsync(
        Project project, DateTimeOffset closedHour, CancellationToken cancellationToken)
    {
        var from = closedHour - Tallying.Baseline;

        var oldest = await tallies.OldestHourAsync(project.Id, cancellationToken);
        if (oldest is null || oldest.Value > from)
        {
            // Either nothing has ever arrived for this project — a project
            // created and not yet deployed is not an incident — or it has not
            // been receiving long enough to have a longest quiet stretch worth
            // multiplying.
            return null;
        }

        var received = await tallies.ReadAsync(
            project.Id, from, closedHour.AddHours(1), cancellationToken);

        if (received.Count == 0)
        {
            // Nothing in a whole fortnight. Its longest quiet stretch is the
            // fortnight itself, so there is no stretch it has come back from and
            // nothing to measure this silence against — whatever this project
            // was, it stopped being it before the window this condition can see.
            return null;
        }

        var hours = Quiet.Hours(received[^1].Hour, closedHour);
        var tolerated = Quiet.Tolerated([.. received.Select(row => row.Hour)], from);

        var level = Quiet.Fires(hours, tolerated) ? Alerting.Holding : Alerting.Clear;

        return await Firing.DecideAsync(
            states,
            project.Id,
            AlertCondition.GoneQuiet,
            level,
            clock.GetUtcNow(),
            cancellationToken)
            ? new Alert.ProjectGoneQuiet(project.Id, project.Name, hours, tolerated)
            : null;
    }
}
