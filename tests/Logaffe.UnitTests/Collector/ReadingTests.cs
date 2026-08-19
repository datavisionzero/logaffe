using System.Text.Json;
using Logaffe.Application.Operations;
using Logaffe.Collector;

namespace Logaffe.UnitTests.Collector;

/// <summary>
/// What a collector puts on the wire, read by the thing that reads it.
/// </summary>
/// <remarks>
/// <para>
/// The collector references none of the four layers and the installation knows
/// nothing of the collector, so the wire format is the whole of what they agree
/// on and there is no compiler holding them to it. <b>This is the test that
/// does</b>: a reading is written by the one and parsed by the other, in one
/// process, and a member renamed on either side fails here.
/// </para>
/// <para>
/// It is the reason both live in this project rather than in two
/// (<c>docs/codebase.md</c>).
/// </para>
/// </remarks>
public sealed class ReadingTests
{
    private static readonly Reading Sample = new(
        Cpu: 0.42,
        MemoryUsed: 6_115_295_232,
        MemoryTotal: 16_769_712_128,
        Load1: 0.52,
        Load5: 0.61,
        Load15: 0.58,
        Filesystems: [new Filesystem("/", 41_234_567_890, 107_374_182_400)]);

    [Fact]
    public void A_reading_this_collector_writes_is_one_the_installation_reads()
    {
        Assert.True(SampleReading.TryRead(Sample.ToJson(), out var read, out var reason), reason);

        Assert.Equal(0.42, read.Cpu, 6);
        Assert.Equal(6_115_295_232, read.MemoryUsed);
        Assert.Equal(16_769_712_128, read.MemoryTotal);
        Assert.Equal(0.52, read.Load1);
        Assert.Equal(0.61, read.Load5);
        Assert.Equal(0.58, read.Load15);

        var filesystem = Assert.Single(read.Filesystems);
        Assert.Equal("/", filesystem.MountPath.Value);
        Assert.Equal(41_234_567_890, filesystem.Used);
        Assert.Equal(107_374_182_400, filesystem.Total);
    }

    [Fact]
    public void It_carries_no_clock_and_no_host()
    {
        using var document = JsonDocument.Parse(Sample.ToJson());

        // The installation stamps a sample when it arrives, and the token says
        // which machine it is. A field for either would be a field somebody
        // eventually trusts (`docs/metrics.md`).
        var members = document.RootElement.EnumerateObject().Select(one => one.Name).ToArray();

        Assert.Equal(
            ["cpu", "memoryUsed", "memoryTotal", "load1", "load5", "load15", "filesystems"],
            members);
    }

    [Fact]
    public void A_collector_watching_no_filesystem_writes_the_member_all_the_same()
    {
        var bare = Sample with { Filesystems = [] };

        Assert.True(SampleReading.TryRead(bare.ToJson(), out var read, out var reason), reason);
        Assert.Empty(read.Filesystems);

        using var document = JsonDocument.Parse(bare.ToJson());
        Assert.Equal(
            JsonValueKind.Array,
            document.RootElement.GetProperty("filesystems").ValueKind);
    }

    [Fact]
    public void The_share_is_written_to_the_precision_it_was_measured_at()
    {
        // Counted off a minute of jiffies: the digits past four places are
        // arithmetic rather than measurement, and they are body nobody reads.
        var noisy = Sample with { Cpu = 1.0 / 3.0 };

        using var document = JsonDocument.Parse(noisy.ToJson());

        Assert.Equal(0.3333, document.RootElement.GetProperty("cpu").GetDouble());
    }

    [Fact]
    public void A_reading_is_far_inside_what_a_delivery_may_be()
    {
        // The cap is generous by three orders of magnitude, and a collector that
        // could approach it would be a collector reporting something else.
        var many = Sample with
        {
            Filesystems = [.. Enumerable.Range(0, 32).Select(
                at => new Filesystem($"/mnt/disk{at}", 41_234_567_890, 107_374_182_400))],
        };

        Assert.True(many.ToJson().Length < 4096);
        Assert.True(SampleReading.TryRead(many.ToJson(), out _, out var reason), reason);
    }
}
