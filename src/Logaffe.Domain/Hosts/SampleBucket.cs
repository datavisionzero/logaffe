namespace Logaffe.Domain.Hosts;

/// <summary>
/// One span of a read of a host's samples, carrying both the average across it
/// and the highest reading in it.
/// </summary>
/// <remarks>
/// <para>
/// The peak rides beside the average because an average is precisely what hides
/// the spike that was worth finding: a minute at the ceiling inside an hour of
/// quiet disappears into a mean and is the whole reason somebody looked.
/// </para>
/// <para>
/// <see cref="MemoryTotal"/> is not averaged. It is how large the machine is
/// rather than how much of it was in use, so the largest value seen in the span
/// is the honest one — a mean of it would only ever be an artefact of a machine
/// being resized mid-span.
/// </para>
/// </remarks>
/// <param name="Start">
/// The beginning of the span. Buckets are contiguous and equal, so the next
/// one's start is this one's end.
/// </param>
public sealed record SampleBucket(
    DateTimeOffset Start,
    double CpuAverage,
    double CpuPeak,
    long MemoryUsedAverage,
    long MemoryUsedPeak,
    long MemoryTotal,
    double LoadAverage,
    double LoadPeak);

/// <summary>
/// One span of a read of one of a host's filesystems.
/// </summary>
/// <param name="Total">
/// How large the filesystem is, taken as the largest in the span for
/// <see cref="SampleBucket.MemoryTotal"/>'s reason.
/// </param>
public sealed record FilesystemBucket(
    DateTimeOffset Start,
    MountPath MountPath,
    long UsedAverage,
    long UsedPeak,
    long Total);

/// <summary>
/// What a host reported over a range, bucketed: the machine's own numbers, and
/// each of its filesystems.
/// </summary>
/// <remarks>
/// A range with nothing in it is two empty lists rather than an absence. A
/// machine that was switched off reported nothing, which is an answer, and the
/// band draws the gap rather than drawing through it.
/// </remarks>
public sealed record SampleWindow(
    IReadOnlyList<SampleBucket> Samples,
    IReadOnlyList<FilesystemBucket> Filesystems);
