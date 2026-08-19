using Logaffe.Application.Ports;
using Logaffe.Domain.Hosts;

namespace Logaffe.Application.Operations;

/// <summary>
/// What became of a delivery of samples.
/// </summary>
public enum SampleOutcome
{
    /// <summary>The reading is stored.</summary>
    Stored,

    /// <summary>
    /// The body was not a reading, and <see cref="SampleReceipt.Reason"/> says
    /// in what way. Nothing was stored — a sample is taken whole or not at all.
    /// </summary>
    NotAReading,

    /// <summary>
    /// The body was over <see cref="Sampling.SampleBytes"/>, and nothing in it
    /// was read. A reading is a few hundred bytes, so this is something else
    /// arriving at this endpoint.
    /// </summary>
    OverTheHardLimit,
}

/// <summary>
/// What a delivery of samples is answered with.
/// </summary>
/// <remarks>
/// Nothing in a collector's control flow depends on it. It exists for the one
/// moment anybody reads it, which is a person wiring up a collector by hand and
/// wanting to know which member they got wrong.
/// </remarks>
public sealed record SampleReceipt(SampleOutcome Outcome, string Reason);

/// <summary>
/// Takes a collector's delivery: reads the one reading in it, stamps it with the
/// installation's clock, and writes it with the filesystem readings taken
/// alongside.
/// </summary>
/// <remarks>
/// <para>
/// <b>The clock is ours and there is only one.</b> An entry has two because a
/// sender's clock can be wrong about when something happened and retention may
/// not count from a number the sender chose (ADR 0007). A sample has nothing to
/// bridge: delivery is fire-and-forget with no buffer and no retry, so a reading
/// is at most a second old when it lands. What the single clock removes is a
/// collector whose clock is a year fast writing samples the sweep will never
/// reach.
/// </para>
/// <para>
/// <b>The moment is rounded down to the interval.</b> A collector posting at
/// 12:00:59 and again at 12:01:01 is reporting two minutes, not two seconds, and
/// without rounding the natural key would take both — which is the doubled
/// machine the key exists to prevent. Rounding makes the key say what the
/// product says: one reading per host per minute, whatever the collector's timer
/// drifted to.
/// </para>
/// </remarks>
public sealed class IngestSample(ISamples samples, TimeProvider clock)
{
    public async Task<SampleReceipt> ExecuteAsync(
        Guid hostId, ReadOnlyMemory<byte> body, CancellationToken cancellationToken)
    {
        if (body.Length > Sampling.SampleBytes)
        {
            return new SampleReceipt(SampleOutcome.OverTheHardLimit, string.Empty);
        }

        if (!SampleReading.TryRead(body.Span, out var reading, out var reason))
        {
            return new SampleReceipt(SampleOutcome.NotAReading, reason);
        }

        var receiptTime = RoundedDown(clock.GetUtcNow());

        var sample = new Sample
        {
            HostId = hostId,
            ReceiptTime = receiptTime,
            Cpu = reading.Cpu,
            MemoryUsed = reading.MemoryUsed,
            MemoryTotal = reading.MemoryTotal,
            Load1 = reading.Load1,
            Load5 = reading.Load5,
            Load15 = reading.Load15,
        };

        var filesystems = reading.Filesystems
            .Select(filesystem => new FilesystemReading
            {
                HostId = hostId,
                ReceiptTime = receiptTime,
                MountPath = filesystem.MountPath,
                Used = filesystem.Used,
                Total = filesystem.Total,
            })
            .ToList();

        await samples.WriteAsync(sample, filesystems, cancellationToken);

        return new SampleReceipt(SampleOutcome.Stored, string.Empty);
    }

    /// <inheritdoc cref="IngestSample"/>
    private static DateTimeOffset RoundedDown(DateTimeOffset moment) =>
        new(moment.Ticks - (moment.Ticks % Sampling.Interval.Ticks), moment.Offset);
}
