using Logaffe.Application.Ports;
using Logaffe.Domain.Alerts;
using Logaffe.Domain.Projects;

namespace Logaffe.Application.Operations;

/// <summary>
/// Whether a project's entries at <c>Error</c> or above are far above what that
/// hour of the day normally holds for it, on two closed hours in a row.
/// </summary>
/// <remarks>
/// <para>
/// This is the one that says a service started failing without anybody going to
/// look, and it is the reason a second error tracker is worth running beside an
/// installation that does not have it. It runs on the tally's second number —
/// counted at the moment the entries were already in hand — so it reads no
/// entry, exactly as the other three read none (ADR 0049).
/// </para>
/// <para>
/// <b>Two hours is what makes it defensible.</b> A deployment's error spike is
/// minutes long and lands inside one hour, and a retry storm that resolves
/// itself never reaches a second one — so neither says anything, not because the
/// burst was filtered but because it stopped. What is left is the shape worth a
/// notification: a provider down, an endpoint refusing everything, something
/// that is still failing an hour after it started.
/// </para>
/// <para>
/// <b>Each hour is measured against its own hour of the day</b>, so this asks
/// for two baselines and reaches an hour further back than the flood condition
/// does. Nine in the morning and eight in the morning are different hours for a
/// project, and holding the second up against the first's normal would judge one
/// of them by a figure that was never about it.
/// </para>
/// </remarks>
public sealed class CheckAProjectIsFailing(
    ITallies tallies, IConditionStates states, TimeProvider clock)
{
    /// <summary>
    /// The alert this closed hour warrants for <paramref name="project"/>, or
    /// <c>null</c>.
    /// </summary>
    public async Task<Alert?> ExecuteAsync(
        Project project, DateTimeOffset closedHour, CancellationToken cancellationToken)
    {
        var previousHour = closedHour.AddHours(-1);

        // A fortnight behind the earlier of the two hours, because both are
        // judged and the earlier one's baseline reaches furthest back.
        var from = previousHour.AddDays(-Baseline.Days);

        var oldest = await tallies.OldestHourAsync(project.Id, cancellationToken);
        if (oldest is null || oldest.Value > from)
        {
            // The same guard the other rate condition has: a project whose
            // oldest row is younger than the fortnight has no normal, so it has
            // no alarm — however it behaves.
            return null;
        }

        var counted = await tallies.ReadAsync(
            project.Id, from, closedHour.AddHours(1), cancellationToken);

        var byHour = counted.ToDictionary(row => row.Hour, row => row.AtErrorOrAbove);

        var errors = byHour.GetValueOrDefault(closedHour);
        var previous = byHour.GetValueOrDefault(previousHour);

        var holds = Fires(byHour, closedHour, errors) && Fires(byHour, previousHour, previous);

        var level = holds ? Alerting.Holding : Alerting.Clear;

        return await Firing.DecideAsync(
            states,
            project.Id,
            AlertCondition.Failing,
            level,
            clock.GetUtcNow(),
            cancellationToken)
            ? new Alert.ProjectFailing(
                project.Id,
                project.Name,
                closedHour,
                errors,
                previous,
                BaselineFor(byHour, closedHour))
            : null;
    }

    private static bool Fires(
        IReadOnlyDictionary<DateTimeOffset, long> byHour, DateTimeOffset hour, long errors) =>
        Failure.Fires(errors, BaselineFor(byHour, hour));

    /// <remarks>
    /// An hour with no tally row counts as nought rather than being left out: a
    /// project that never fails at four in the morning normally has no errors at
    /// four in the morning, which is a figure and not a gap.
    /// </remarks>
    private static long BaselineFor(
        IReadOnlyDictionary<DateTimeOffset, long> byHour, DateTimeOffset hour) =>
        Baseline.Of(
        [
            .. Enumerable
                .Range(1, Baseline.Days)
                .Select(day => byHour.GetValueOrDefault(hour.AddDays(-day))),
        ]);
}
