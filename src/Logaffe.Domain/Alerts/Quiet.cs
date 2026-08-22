namespace Logaffe.Domain.Alerts;

/// <summary>
/// The arithmetic behind a project having gone quiet: how long it has been
/// silent, and how long its own fortnight says silence is ordinary for it.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here is entered by the operator. What a project tolerates is derived
/// from what that project has already done, which is the whole reason the
/// conditions can be a closed set (ADR 0050): a project that is idle every night
/// is described by its nights rather than woken for them.
/// </para>
/// <para>
/// <b>A late true alarm beats a false one</b>, and this is where that trade is
/// spent. A project delivering every hour is noticed on its second silent hour;
/// one that is idle five hours a night tolerates fifteen, so an outage at
/// breakfast is noticed some time after midnight. The alternative is a project
/// whose alerts the operator learns to ignore, which is worth less than none.
/// </para>
/// </remarks>
public static class Quiet
{
    /// <summary>
    /// How many times the project's longest quiet stretch it has to be silent
    /// for before that silence is an incident.
    /// </summary>
    public const int Multiple = 3;

    /// <summary>
    /// What is tolerated however continuously a project delivers, so that a
    /// project with no gap at all is not woken for its first silent hour.
    /// </summary>
    public const int LeastTolerated = 1;

    /// <summary>
    /// How many whole closed hours have passed since <paramref name="newest"/>,
    /// the most recent hour the project has a tally row for.
    /// </summary>
    public static int Hours(DateTimeOffset newest, DateTimeOffset closedHour) =>
        (int)Math.Max(0, (closedHour - newest).TotalHours);

    /// <summary>
    /// How many silent hours this project's own fortnight says are ordinary:
    /// its longest quiet stretch, three times over.
    /// </summary>
    /// <param name="received">
    /// The hours the project has a tally row for, oldest first, from
    /// <paramref name="from"/> up to and including the hour being evaluated.
    /// </param>
    /// <param name="from">The first hour of the fortnight being measured.</param>
    /// <remarks>
    /// <para>
    /// <b>The silence in progress is not one of the stretches.</b> The runs
    /// counted are the ones the project has since come back from — the gaps
    /// between the hours it delivered in, and the one before the first of them —
    /// and the hours since its last delivery are what is being judged rather
    /// than part of what judges it. Counting those too would make the tolerance
    /// grow with the outage and no project could ever be quiet enough to fire.
    /// </para>
    /// <para>
    /// The stretch before the first row is counted whole even though it may run
    /// back past <paramref name="from"/>. It is a fortnight the condition looks
    /// at, and a gap that started before it is a gap the fortnight can only
    /// under-state — which errs towards the alarm that arrives late.
    /// </para>
    /// </remarks>
    public static int Tolerated(IReadOnlyList<DateTimeOffset> received, DateTimeOffset from)
    {
        var longest = 0;
        var previous = from.AddHours(-1);

        foreach (var hour in received)
        {
            longest = Math.Max(longest, (int)(hour - previous).TotalHours - 1);
            previous = hour;
        }

        return Math.Max(LeastTolerated, longest * Multiple);
    }

    /// <summary>
    /// Whether a project silent for <paramref name="quiet"/> hours is quiet
    /// enough to say something about.
    /// </summary>
    public static bool Fires(int quiet, int tolerated) => quiet > tolerated;
}
