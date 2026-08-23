using Logaffe.Domain.Projects;

namespace Logaffe.Domain.Alerts;

/// <summary>
/// What a project's own recent history says is normal for it: the median of one
/// hour of the day across the fortnight behind it.
/// </summary>
/// <remarks>
/// <para>
/// It is the whole reason the set of conditions can be closed. A threshold is a
/// number the operator would have to guess about a quantity they have never
/// looked at — how many entries an hour is normal for <c>shop / api</c> at three
/// in the morning — and every wrong guess is a false alarm. Nothing here is
/// entered, so nothing here is entered wrong (ADR 0050).
/// </para>
/// <para>
/// <b>It is shared by both rate conditions and it is the same arithmetic for
/// both.</b> What differs between a project delivering far more than it does and
/// one failing far more than it does is which of the tally's two numbers is
/// counted and what floor sits under the ratio — not what "normally" means. A
/// second median that drifted from this one would be two answers to one
/// question.
/// </para>
/// </remarks>
public static class Baseline
{
    /// <summary>
    /// How many days back the same hour of the day is taken from, which is the
    /// fortnight the tally is kept a baseline's worth of.
    /// </summary>
    public static int Days => (int)Tallying.Baseline.TotalDays;

    /// <summary>
    /// The median of <paramref name="hours"/>, which is what that hour of the
    /// day normally holds.
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
    public static long Of(IReadOnlyList<long> hours)
    {
        if (hours.Count == 0)
        {
            return 0;
        }

        var sorted = hours.Order().ToList();

        return sorted[(sorted.Count - 1) / 2];
    }
}
