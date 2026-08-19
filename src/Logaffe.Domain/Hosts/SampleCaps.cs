namespace Logaffe.Domain.Hosts;

/// <summary>
/// The sizes a sample delivery may not exceed.
/// </summary>
/// <remarks>
/// Product values, like <see cref="Entries.Caps"/>: the same in every
/// installation and not something the operator tunes. They are small because the
/// schema is closed (ADR 0044) — there is a known number of numbers in a reading,
/// so a delivery that is larger than this is not a reading that grew, it is
/// something else arriving at this endpoint.
/// </remarks>
public static class SampleCaps
{
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
