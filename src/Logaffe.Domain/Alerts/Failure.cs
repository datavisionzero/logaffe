namespace Logaffe.Domain.Alerts;

/// <summary>
/// The arithmetic behind a project failing far more than it does: what counts as
/// far more, and how long it has to keep being true before anything is said.
/// </summary>
/// <remarks>
/// <para>
/// It is <see cref="Flood"/> narrowed to a level and slowed down. The ratio and
/// the <see cref="Baseline"/> behind it are the same, because what "normally"
/// means does not change with which of the tally's two numbers is being counted;
/// what changes is the floor, and that a single hour is not enough.
/// </para>
/// <para>
/// <b><see cref="ConsecutiveHours"/> is the whole of what is new, and it is what
/// answers the two objections this condition was deferred on.</b> A deployment's
/// error spike is minutes long and lands inside one hour, and a retry storm that
/// resolves itself never reaches a second one — so neither fires, not because
/// the burst was filtered but because it stopped. A storm that is not over is
/// precisely the thing an operator should be told about.
/// </para>
/// </remarks>
public static class Failure
{
    /// <summary>
    /// How many times its own hour a closed hour's errors have to be, which is
    /// <see cref="Flood.Multiple"/> and deliberately the same number.
    /// </summary>
    public const int Multiple = Flood.Multiple;

    /// <summary>
    /// The floor under the ratio, in entries at <c>Error</c> or above, whatever
    /// the ratio says.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the one number in the condition that nothing derives</b>, and
    /// it is worth saying so rather than dressing it up. <see cref="Flood.Floor"/>
    /// is equally a judgement, so it is not a new kind of number here — but a
    /// thousand is right for entries, because entries are cheap, and wrong for
    /// errors: a project taking twenty payments a day would never reach it and
    /// the condition would be decorative.
    /// </para>
    /// <para>
    /// What mitigates it rather than solving it is where it applies. It only
    /// decides anything where the ratio has already passed, and only where the
    /// hour before it passed too.
    /// </para>
    /// </remarks>
    public const long Floor = 10;

    /// <summary>
    /// How many closed hours in a row have to hold before anything is said.
    /// </summary>
    /// <remarks>
    /// <b>It is bought with latency, deliberately.</b> A failure beginning at
    /// the top of an hour is said two hours later; one beginning at the end of
    /// an hour, just over one; and one beginning too late in an hour to reach
    /// <see cref="Floor"/> in what is left of it does not qualify that hour at
    /// all and is said up to three hours later. That is the trade the whole of
    /// <c>docs/alerts.md</c> is built on — a late true alarm beats a false one —
    /// and it is the same arithmetic a project going quiet already pays.
    /// </remarks>
    public const int ConsecutiveHours = 2;

    /// <summary>
    /// Whether one closed hour of <paramref name="errors"/> is far enough above
    /// <paramref name="baseline"/> to count towards saying something. Both of
    /// <see cref="ConsecutiveHours"/> have to answer yes.
    /// </summary>
    public static bool Fires(long errors, long baseline) =>
        errors >= Floor && errors > baseline * Multiple;
}
