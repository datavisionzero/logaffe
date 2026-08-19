using Logaffe.Application.Ports;
using Logaffe.Domain.Hosts;
using Microsoft.EntityFrameworkCore;

namespace Logaffe.Infrastructure.Persistence;

/// <summary>
/// Reading what a host reported, bucketed on the way out.
/// </summary>
/// <remarks>
/// <para>
/// One grouped statement over the leading columns of <c>pk_host_sample</c>, and
/// a second over <c>pk_filesystem_reading</c>. The bucketing is done here rather
/// than by the caller because the alternative is ten thousand rows crossing a
/// layer boundary on their way to being averaged.
/// </para>
/// <para>
/// The grouping expression is composed in LINQ rather than written as SQL,
/// unlike the entry reader's queries. The reason the entry path is hand-written
/// is that its statements are shaped by indexes whose cost <c>docs/storage.md</c>
/// measures and re-reading them is the standing price of changing one; these are
/// two range scans over a natural key with nothing to choose between plans.
/// </para>
/// </remarks>
public sealed class SampleReader(LogaffeDbContext context) : ISampleReader
{
    public async Task<SampleWindow> ReadAsync(
        Guid hostId,
        DateTimeOffset from,
        DateTimeOffset to,
        BucketCount buckets,
        CancellationToken cancellationToken)
    {
        // Each bucket is the range divided by the count, floored to a whole
        // number of ticks. A range shorter than the count gives every bucket one
        // tick, which is harmless: what comes back is the samples that exist,
        // and there are at most a handful in a range that short.
        var span = (to - from) / buckets.Value;
        if (span <= TimeSpan.Zero)
        {
            span = TimeSpan.FromTicks(1);
        }

        var ticks = span.Ticks;
        var origin = from.UtcTicks;

        var samples = await context.Samples
            .AsNoTracking()
            .Where(s => s.HostId == hostId && s.ReceiptTime >= from && s.ReceiptTime <= to)
            .GroupBy(s => (s.ReceiptTime.UtcTicks - origin) / ticks)
            .Select(bucket => new
            {
                Index = bucket.Key,
                CpuAverage = bucket.Average(s => s.Cpu),
                CpuPeak = bucket.Max(s => s.Cpu),
                MemoryUsedAverage = bucket.Average(s => (double)s.MemoryUsed),
                MemoryUsedPeak = bucket.Max(s => s.MemoryUsed),
                MemoryTotal = bucket.Max(s => s.MemoryTotal),
                LoadAverage = bucket.Average(s => s.Load1),
                LoadPeak = bucket.Max(s => s.Load1),
            })
            .OrderBy(bucket => bucket.Index)
            .ToListAsync(cancellationToken);

        var filesystems = await context.FilesystemReadings
            .AsNoTracking()
            .Where(r => r.HostId == hostId && r.ReceiptTime >= from && r.ReceiptTime <= to)
            .GroupBy(r => new
            {
                Index = (r.ReceiptTime.UtcTicks - origin) / ticks,
                r.MountPath,
            })
            .Select(bucket => new
            {
                bucket.Key.Index,
                bucket.Key.MountPath,
                UsedAverage = bucket.Average(r => (double)r.Used),
                UsedPeak = bucket.Max(r => r.Used),
                Total = bucket.Max(r => r.Total),
            })
            .OrderBy(bucket => bucket.Index)
            .ThenBy(bucket => bucket.MountPath)
            .ToListAsync(cancellationToken);

        return new SampleWindow(
            [
                .. samples.Select(bucket => new SampleBucket(
                    from + (span * bucket.Index),
                    bucket.CpuAverage,
                    bucket.CpuPeak,
                    (long)bucket.MemoryUsedAverage,
                    bucket.MemoryUsedPeak,
                    bucket.MemoryTotal,
                    bucket.LoadAverage,
                    bucket.LoadPeak)),
            ],
            [
                .. filesystems.Select(bucket => new FilesystemBucket(
                    from + (span * bucket.Index),
                    bucket.MountPath,
                    (long)bucket.UsedAverage,
                    bucket.UsedPeak,
                    bucket.Total)),
            ]);
    }

    /// <remarks>
    /// One grouped statement rather than one lookup per host: samples are not
    /// scoped the way entries are (ADR 0045), so unlike the project list's
    /// equivalent there is no per-host reader standing in the way of asking for
    /// all of them at once.
    /// </remarks>
    public async Task<IReadOnlyDictionary<Guid, DateTimeOffset>> LastReportedAsync(
        CancellationToken cancellationToken) =>
        await context.Samples
            .AsNoTracking()
            .GroupBy(s => s.HostId)
            .Select(host => new { HostId = host.Key, Last = host.Max(s => s.ReceiptTime) })
            .ToDictionaryAsync(row => row.HostId, row => row.Last, cancellationToken);
}
