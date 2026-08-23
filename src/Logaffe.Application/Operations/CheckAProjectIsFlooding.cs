using Logaffe.Application.Ports;
using Logaffe.Domain.Alerts;
using Logaffe.Domain.Projects;

namespace Logaffe.Application.Operations;

/// <summary>
/// Whether a project's closed hour is far above what that hour of the day
/// normally holds for it.
/// </summary>
/// <remarks>
/// <para>
/// This is the one that fills the disk while nobody is watching: a retry storm,
/// a loop logging per iteration, a debug level left on after a deploy. It is the
/// closed hour against the same hour of the day on each of the fourteen days
/// before it, and an hour with no row counts as nought — a project that is
/// normally silent at three in the morning is normally silent rather than absent
/// from the arithmetic.
/// </para>
/// <para>
/// <b>The floor is what makes a baseline of nought safe.</b> Ten times nothing
/// is nothing, so without a thousand entries under it every first entry of a
/// quiet hour would fire; with it, a project that has never written anything at
/// four in the morning writing five thousand entries at four in the morning is
/// exactly what this says something about.
/// </para>
/// </remarks>
public sealed class CheckAProjectIsFlooding(
    ITallies tallies, IConditionStates states, TimeProvider clock)
{
    /// <summary>
    /// The alert this closed hour warrants for <paramref name="project"/>, or
    /// <c>null</c>.
    /// </summary>
    public async Task<Alert?> ExecuteAsync(
        Project project, DateTimeOffset closedHour, CancellationToken cancellationToken)
    {
        var from = closedHour.AddDays(-Baseline.Days);

        var oldest = await tallies.OldestHourAsync(project.Id, cancellationToken);
        if (oldest is null || oldest.Value > from)
        {
            // A project whose oldest row is younger than the fortnight has no
            // normal, so it has no alarm — two entries becoming twenty is a
            // tenfold rise and a project's first busy hour is not an incident.
            return null;
        }

        var counted = await tallies.ReadAsync(
            project.Id, from, closedHour.AddHours(1), cancellationToken);

        var byHour = counted.ToDictionary(row => row.Hour, row => row.Entries);

        var entries = byHour.GetValueOrDefault(closedHour);
        var baseline = Baseline.Of(
        [
            .. Enumerable
                .Range(1, Baseline.Days)
                .Select(day => byHour.GetValueOrDefault(closedHour.AddDays(-day))),
        ]);

        var level = Flood.Fires(entries, baseline) ? Alerting.Holding : Alerting.Clear;

        return await Firing.DecideAsync(
            states,
            project.Id,
            AlertCondition.Flooding,
            level,
            clock.GetUtcNow(),
            cancellationToken)
            ? new Alert.ProjectFlooding(
                project.Id, project.Name, closedHour, entries, baseline)
            : null;
    }
}
