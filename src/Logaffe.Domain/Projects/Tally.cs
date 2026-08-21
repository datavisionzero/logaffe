namespace Logaffe.Domain.Projects;

/// <summary>
/// What one project received in one hour: how many entries arrived, and how many
/// of those were <c>Error</c> or worse.
/// </summary>
/// <remarks>
/// <para>
/// It is counted as the deliveries arrive rather than by asking the entries
/// afterwards (ADR 0047). A count over the largest table in the database is what
/// <c>docs/ui.md</c> refuses a dashboard for; a history is the shape a count is
/// worst at, being the same number wanted every hour for as long as the
/// installation runs.
/// </para>
/// <para>
/// <b>It is two numbers, and that is closed.</b> Not per logger name, per
/// instance, per level or per host — each of those is the labelled series ADR
/// 0044 refused for samples, arriving by the back door on the table with the
/// highest cardinality in the product. A third number is a change to ADR 0047
/// and a migration.
/// </para>
/// <para>
/// <b>Unlike a log entry, it is updated — until its hour has passed.</b> An
/// entry is written once and leaves only by ageing out; a tally accumulates for
/// as long as its hour is open, and stops changing on its own when the hour
/// does. Nothing rewrites a closed one.
/// </para>
/// <para>
/// <b>It is not exact and nothing reconciles it.</b> The counter it comes from
/// lives in memory and is written down once a minute, so a restart may leave an
/// hour short. Nothing counts it against the entries and no job repairs a gap —
/// which is affordable because of what it is for: a baseline over a fortnight
/// and a footprint in gibibytes, neither of which turns on a few hundred rows.
/// </para>
/// </remarks>
public sealed class Tally
{
    private readonly DateTimeOffset _hour;

    private Tally()
    {
    }

    /// <summary>
    /// The project this counts, by the identity that survives its rename.
    /// </summary>
    /// <remarks>
    /// It leads the key, as it leads every index on the entry table and for the
    /// same reason: everything that reads this reads one project's history, and
    /// a key that did not lead with it would make each of them pay for every
    /// other project's hours.
    /// </remarks>
    public required Guid ProjectId { get; init; }

    /// <summary>
    /// The hour these arrived in, counted on the receipt clock — the only one of
    /// an entry's two clocks a sender cannot get wrong (ADR 0007), and the one
    /// retention already counts from.
    /// </summary>
    public required DateTimeOffset Hour
    {
        get => _hour;
        init => _hour = WholeHour(value);
    }

    /// <summary>How many entries arrived for the project in that hour.</summary>
    public long Entries { get; private set; }

    /// <summary>
    /// How many of those were <c>Error</c> or worse. It rides beside the total
    /// because it costs one comparison at the moment the entries are already in
    /// hand, and because the condition ADR 0050 defers is the one that will need
    /// it.
    /// </summary>
    public long AtErrorOrAbove { get; private set; }

    /// <summary>An hour nothing has been counted into yet.</summary>
    public static Tally For(Guid projectId, DateTimeOffset hour) =>
        new() { ProjectId = projectId, Hour = hour };

    /// <summary>
    /// Adds what a flush counted. Both figures are amounts rather than totals,
    /// and neither may be negative: the counter this comes from only ever goes
    /// up, and a tally that could be reduced would be a correction — which is
    /// the thing nothing here does.
    /// </summary>
    public void Add(long entries, long atErrorOrAbove)
    {
        Entries += NotNegative(entries, nameof(entries));
        AtErrorOrAbove += NotNegative(atErrorOrAbove, nameof(atErrorOrAbove));
    }

    /// <remarks>
    /// The offset is checked as well as the instant, and it is not redundant:
    /// two o'clock somewhere else is the same moment as midday here, so
    /// comparing them alone would let a row in whose hour reads as fourteen. The
    /// condition of ADR 0050 that measures an hour against the same hour of
    /// other days would then be reading a clock nobody has.
    /// </remarks>
    private static DateTimeOffset WholeHour(DateTimeOffset value) =>
        value.Offset == TimeSpan.Zero && value == Tallying.HourOf(value)
            ? value
            : throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "A tally's hour is a whole hour at UTC.");

    private static long NotNegative(long value, string name) =>
        value >= 0
            ? value
            : throw new ArgumentOutOfRangeException(
                name, value, $"A tally's {name} is not negative.");
}
