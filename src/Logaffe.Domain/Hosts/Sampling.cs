namespace Logaffe.Domain.Hosts;

/// <summary>
/// What is true of every sample in every installation.
/// </summary>
/// <remarks>
/// <b>Product values</b>, like <see cref="Entries.Caps"/>: documented in
/// <c>docs/metrics.md</c> and not something the operator tunes. The sizes are
/// small because the schema is closed (ADR 0044) — there is a known number of
/// numbers in a reading, so a delivery larger than this is not a reading that
/// grew, it is something else arriving at this endpoint.
/// </remarks>
public static class Sampling
{
    /// <summary>
    /// How often a collector reports. It is the product's and not a setting: a
    /// number that can be turned up is a number somebody turns up, and the
    /// storage this rests on was sized for one row per host per minute.
    /// </summary>
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    /// <summary>
    /// The window a fresh installation keeps samples for, until the operator
    /// says otherwise. Long enough that last month's incident is still there,
    /// well short of the ceiling, and — unlike a project's entries — the same
    /// for every machine, because there is no reason to keep one machine's
    /// numbers longer than another's.
    /// </summary>
    public const int RetentionDaysByDefault = 30;

    /// <summary>
    /// How many filesystems one sample may carry. The mounts are named in the
    /// collector's configuration, so this is not what bounds them in practice —
    /// it is what stops a delivery from deciding on its own how many rows a
    /// minute a host writes.
    /// </summary>
    public const int FilesystemsPerSample = 32;

    /// <summary>
    /// How large one delivery may be, measured after decompression. A reading of
    /// this shape is a few hundred bytes; the cap is generous by three orders of
    /// magnitude and still nowhere near the entry batch's five mebibytes.
    /// </summary>
    public const int SampleBytes = 64 * 1024;
}
