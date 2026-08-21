namespace Logaffe.Domain.Projects;

/// <summary>
/// What is true of every tally in every installation.
/// </summary>
/// <remarks>
/// <b>Product values</b>, like <see cref="Entries.Caps"/> and
/// <see cref="Hosts.Sampling"/>: written down in <c>docs/alerts.md</c> and not
/// something the operator tunes. There is nothing here to turn up, because
/// nothing here is a trade the operator is in a position to make — the hour is
/// the resolution the two things that read this were designed around, and the
/// period is how far back they may look.
/// </remarks>
public static class Tallying
{
    /// <summary>
    /// How often the counter in memory is written down. A minute is what a
    /// crash may cost, and the tally is not reconciled against the entries
    /// afterwards (ADR 0047) — so this is the whole of the promise about how
    /// current the written figure is.
    /// </summary>
    public static readonly TimeSpan FlushInterval = TimeSpan.FromMinutes(1);

    /// <summary>
    /// How long a tally row is kept, which is deliberately not any project's
    /// retention window.
    /// </summary>
    /// <remarks>
    /// It outlives the entries it counted, and it has to: a project keeping
    /// entries for a week still needs a fortnight of history behind it to have a
    /// baseline at all, and that project is the one most likely to be busy. The
    /// figure is a year and a month — long enough to cover a window at the
    /// ceiling of ADR 0048 with slack, and about ten mebibytes for twenty
    /// projects against the gibibytes of entries it describes.
    /// </remarks>
    public const int RetentionDays = 400;

    /// <summary>
    /// The hour a moment falls in: the same instant with its minutes, seconds
    /// and ticks taken off, at UTC.
    /// </summary>
    /// <remarks>
    /// This is the whole of the key's second half, so it is a rule rather than a
    /// convenience — two writers disagreeing about where an hour starts would
    /// put one hour of one project in two rows, and the two things that read
    /// this both compare an hour against the same hour of other days.
    /// </remarks>
    public static DateTimeOffset HourOf(DateTimeOffset moment)
    {
        var utc = moment.ToUniversalTime();

        return new DateTimeOffset(
            utc.Year, utc.Month, utc.Day, utc.Hour, 0, 0, TimeSpan.Zero);
    }
}
