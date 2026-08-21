using Logaffe.Domain.Hosts;
using Logaffe.Domain.Projects;

namespace Logaffe.Domain.Storage;

/// <summary>
/// What a retention window costs, in the three numbers an operator needs to
/// decide on one: what this installation holds today, what the window they are
/// typing implies, and what the disk under it has left.
/// </summary>
/// <remarks>
/// <para>
/// This is the work the ceiling used to do badly (ADR 0048). Days are not the
/// axis anything is paid for — ninety of them permit one noisy project ninety
/// gibibytes and refuse a quiet one a year that costs two — so the ceiling moved
/// to a year and the field states the cost instead. Which is a better bound,
/// because it is in the units the limit is really in.
/// </para>
/// <para>
/// <b>It is advisory and refuses nothing.</b> Nothing here is compared against
/// anything: there is no quota, no size cap and no drop-oldest, and time stays
/// the only limit a project has (<see cref="RetentionWindow"/>). The operator
/// sees three numbers and decides.
/// </para>
/// <para>
/// <b>Two of the three are absent on an installation that cannot answer
/// them</b>, and absent is a state rather than a failure — a project with less
/// than a fortnight of tally has no rate to extrapolate from, and an
/// installation that has not named the host it runs on has no disk to read. What
/// shows the numbers shows the ones it has.
/// </para>
/// </remarks>
/// <param name="Held">
/// What the whole database holds this moment, exactly, from the store itself
/// rather than from arithmetic. It is every table and every index, not the
/// entries alone: the operator's disk does not distinguish them.
/// </param>
/// <param name="Implied">
/// What the window asked about will hold in steady state, or <c>null</c> when
/// there is nothing to work it out from.
/// </param>
/// <param name="Disk">
/// The filesystem the database sits on, or <c>null</c> when this installation
/// names no host — which is every installation until the operator says
/// otherwise (<c>docs/metrics.md</c>).
/// </param>
public sealed record Footprint(long Held, long? Implied, DiskSpace? Disk)
{
    /// <summary>
    /// What one entry costs, heap and indexes together.
    /// </summary>
    /// <remarks>
    /// The number <c>docs/storage.md</c> measured over ten million entries and
    /// names as the one to multiply for sizing an installation. It is the whole
    /// row's share of the store rather than the width of its columns, which is
    /// why it is more than four times the text most entries carry: on this table
    /// the indexes together are larger than the heap (ADR 0010).
    /// </remarks>
    public const long BytesPerEntry = 1229;

    /// <summary>
    /// What one sample costs, key included: <c>docs/storage.md</c>'s 78 MiB over
    /// the 648 000 rows it is arithmetic for.
    /// </summary>
    public const long BytesPerSample = 126;

    /// <summary>
    /// What one filesystem reading costs, key included, from the same table:
    /// 175 MiB over 1 944 000 rows.
    /// </summary>
    public const long BytesPerFilesystemReading = 94;

    /// <summary>
    /// What a project's entries will hold at <paramref name="window"/>, given
    /// <paramref name="entries"/> counted over <paramref name="over"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The rate comes from the tally (ADR 0047) rather than from counting the
    /// entries, which is the whole reason that table exists: this is a number
    /// wanted while somebody is typing, and a count over the largest table in
    /// the database is the operation likeliest to run out of its five seconds
    /// (ADR 0026).
    /// </para>
    /// <para>
    /// <b>It is steady state and not a forecast.</b> What comes back is what the
    /// window holds if the project keeps doing what it has been doing, which is
    /// the only thing arithmetic can say about a project's future traffic. A
    /// project that trebles next week costs three times this.
    /// </para>
    /// </remarks>
    public static long OfEntries(long entries, TimeSpan over, RetentionWindow window)
    {
        if (entries < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(entries), entries, "A count of entries is not negative.");
        }

        if (over <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(over), over, "A rate is counted over a period with length.");
        }

        return (long)Math.Round(entries / over.TotalDays * window.Days * BytesPerEntry);
    }

    /// <summary>
    /// What the sample tables will hold at <paramref name="window"/>, for
    /// <paramref name="hosts"/> machines reporting <paramref name="filesystems"/>
    /// filesystems between them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Arithmetic from what is reporting rather than an average of what arrived,
    /// which is the difference between these tables and the entries: a collector
    /// writes one row a minute and its filesystems' rows beside it
    /// (<see cref="Sampling.Interval"/>), so the rate is the product's and not a
    /// thing to be measured. It is the arithmetic <c>docs/storage.md</c> does for
    /// these tables itself, with this installation's shape in it.
    /// </para>
    /// <para>
    /// <b>A machine that is switched off counts.</b> The hosts are the ones this
    /// installation collects from, not the ones that happened to report in the
    /// last minute — a machine down for the afternoon does not make the window
    /// cheaper, and a window is being chosen for the months after it.
    /// </para>
    /// </remarks>
    public static long OfSamples(int hosts, int filesystems, RetentionWindow window)
    {
        if (hosts < 0 || filesystems < 0)
        {
            throw new ArgumentOutOfRangeException(
                hosts < 0 ? nameof(hosts) : nameof(filesystems),
                hosts < 0 ? hosts : filesystems,
                "A count of what reports is not negative.");
        }

        var aDay = (long)(TimeSpan.FromDays(1) / Sampling.Interval);

        return window.Days * aDay
            * ((hosts * BytesPerSample) + (filesystems * BytesPerFilesystemReading));
    }
}
