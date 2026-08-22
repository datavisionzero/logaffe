using Logaffe.Domain.Projects;

namespace Logaffe.Domain.Alerts;

/// <summary>
/// What is true of every condition in every installation.
/// </summary>
/// <remarks>
/// <b>Product values</b>, like <see cref="Tallying"/> and
/// <see cref="Hosts.Sampling"/>, and here more deliberately than either: a
/// threshold is a number the operator would have to guess about a quantity they
/// have never looked at, and every wrong guess is a false alarm (ADR 0050). The
/// switch and the mute are the whole of what is adjustable, so nothing on this
/// page has a field behind it.
/// </remarks>
public static class Alerting
{
    /// <summary>
    /// How long after an alert nothing more is said about that condition for
    /// that subject, however the condition behaves in between.
    /// </summary>
    /// <remarks>
    /// It is the floor under a second event rather than the whole of what stops
    /// a repeat: a condition that is still holding says nothing more whatever
    /// this is, because it is one event still happening
    /// (<see cref="ConditionState"/>). What this catches is the other shape — a
    /// condition that clears and holds again an hour later, which without it
    /// would notify hourly for as long as it flapped.
    /// </remarks>
    public static readonly TimeSpan Silence = TimeSpan.FromHours(6);

    /// <summary>
    /// The level a condition that either holds or does not fires at.
    /// </summary>
    /// <remarks>
    /// Two of the three conditions are of that shape, and the store filling up
    /// is not: it holds at 85 per cent and again, worse, at 95, and the second
    /// is a thing worth saying while the first is still latched
    /// (<see cref="StoreFullness"/>). A level rather than a flag is what lets
    /// one rule serve both.
    /// </remarks>
    public const int Holding = 1;

    /// <summary>The level of a condition that is not holding.</summary>
    public const int Clear = 0;

    /// <summary>
    /// How recently the machine the installation sits on has to have reported
    /// for its disk to be read off what it said.
    /// </summary>
    /// <remarks>
    /// A collector reports every minute (<see cref="Hosts.Sampling.Interval"/>),
    /// so an hour of silence is sixty missed readings: a machine that has
    /// stopped rather than one that is late. Yesterday's per cent presented as
    /// today's is the number that would be believed, so a stale report is no
    /// report and the condition says it cannot see
    /// (<see cref="Blindness.NotReporting"/>).
    /// </remarks>
    public static readonly TimeSpan Reporting = TimeSpan.FromHours(1);

    /// <summary>
    /// The hour that has just closed at <paramref name="now"/>, which is the
    /// only hour any condition is ever evaluated on.
    /// </summary>
    /// <remarks>
    /// Never the hour in progress: a burst at five past would otherwise look
    /// like twelve times the hour it is a twelfth of (ADR 0050). It is the same
    /// truncation the tally is written under, one hour back.
    /// </remarks>
    public static DateTimeOffset ClosedHourAt(DateTimeOffset now) =>
        Tallying.HourOf(now).AddHours(-1);
}
