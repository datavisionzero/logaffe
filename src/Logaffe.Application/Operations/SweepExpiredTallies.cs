using Logaffe.Application.Ports;
using Logaffe.Domain.Projects;

namespace Logaffe.Application.Operations;

/// <summary>
/// Removes the tally hours that have outlived the period every installation
/// keeps them for, and the hours of projects that no longer exist.
/// </summary>
/// <remarks>
/// <para>
/// <b>The period is the tally's own and not any project's window.</b> A tally
/// row outlives the entries it counted deliberately: a project keeping entries
/// for a week still needs a fortnight of history to have a baseline, and that is
/// the project most likely to be busy (ADR 0047).
/// </para>
/// <para>
/// <b>One statement, where the other two sweeps walk and portion.</b> Those do
/// it because a window is per project and because the entry table is the largest
/// object in the database; neither is true here. Every project's hours expire on
/// the same clock, and a year of twenty projects is under two hundred thousand
/// rows of which a day's worth goes at a time — so a walk would be machinery for
/// a delete that is over before it started.
/// </para>
/// <para>
/// <b>It also takes what a deleted project left.</b> There is no foreign key
/// from a tally to its project, for the reason ADR 0019 gives for the entries
/// not having one, so the walk over the live projects cannot reach these. They
/// go whole rather than by a period, which is the same thing said with a period
/// of nothing.
/// </para>
/// <para>
/// It runs on the retention job's pass rather than a timer of its own — the same
/// concern on the same clock, and the same reason <see cref="SweepExpiredSamples"/>
/// rides there.
/// </para>
/// </remarks>
public sealed class SweepExpiredTallies(IProjects projects, ITallies tallies, TimeProvider clock)
{
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var before = Tallying
            .HourOf(clock.GetUtcNow())
            .AddDays(-Tallying.RetentionDays);

        await tallies.RemoveHoursBeforeAsync(before, cancellationToken);

        var live = await projects.ListAsync(cancellationToken);
        var known = live.Select(project => project.Id).ToHashSet();

        foreach (var projectId in await tallies.ProjectsWithTalliesAsync(cancellationToken))
        {
            if (!known.Contains(projectId))
            {
                await tallies.RemoveProjectAsync(projectId, cancellationToken);
            }
        }
    }
}
