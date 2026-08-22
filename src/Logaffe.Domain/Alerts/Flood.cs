using Logaffe.Domain.Projects;

namespace Logaffe.Domain.Alerts;

/// <summary>
/// The arithmetic behind a project delivering far more than it does: what that
/// hour of the day normally holds for it, and what counts as far more.
/// </summary>
public static class Flood
{
    /// <summary>How many times its own hour a closed hour has to be.</summary>
    public const int Multiple = 10;

    /// <summary>
    /// The floor under the ratio, in entries, whatever the ratio says.
    /// </summary>
    /// <remarks>
    /// <b>It is absolute and it is not a ratio.</b> Two entries becoming twenty
    /// is a tenfold rise and is not an incident, in any project, ever. It is
    /// also what makes a baseline of nought safe: ten times nothing is nothing,
    /// so without this every first entry of a quiet hour would fire.
    /// </remarks>
    public const long Floor = 1000;

    /// <summary>
    /// How many days back the same hour of the day is taken from, which is the
    /// fortnight the tally is kept a baseline's worth of.
    /// </summary>
    public static int Days => (int)Tallying.Baseline.TotalDays;

    /// <summary>
    /// What that hour of the day normally holds: the median of the same hour on
    /// each of the <see cref="Days"/> days before it.
    /// </summary>
    /// <param name="hours">
    /// The <see cref="Days"/> figures, an hour with no tally row counted as
    /// nought — because a project that is normally silent at three in the
    /// morning is normally silent rather than absent from the arithmetic.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>A median by hour of the day, not an average over the day.</b> The
    /// batch job that writes fifty thousand entries at three in the morning is
    /// normal at three in the morning; averaged across the day it would fire
    /// every single night, and it would drag the daytime baseline up until a
    /// real daytime flood fitted underneath it.
    /// </para>
    /// <para>
    /// An even count has two middle figures and the answer is the lower of the
    /// two rather than the mean of them. A baseline is multiplied by ten and
    /// then compared, so the half an average would add is noise on a figure that
    /// only ever decides one thing, and taking the lower of the two keeps the
    /// answer a figure the project actually had.
    /// </para>
    /// </remarks>
    public static long Baseline(IReadOnlyList<long> hours)
    {
        if (hours.Count == 0)
        {
            return 0;
        }

        var sorted = hours.Order().ToList();

        return sorted[(sorted.Count - 1) / 2];
    }

    /// <summary>
    /// Whether a closed hour of <paramref name="entries"/> is far enough above
    /// <paramref name="baseline"/> to say something about.
    /// </summary>
    public static bool Fires(long entries, long baseline) =>
        entries >= Floor && entries > baseline * Multiple;
}
