using System.Buffers;
using System.Text.Json;

namespace Logaffe.Collector;

/// <summary>One filesystem, as this collector was asked to measure it.</summary>
internal sealed record Filesystem(string Mount, long Used, long Total);

/// <summary>
/// One reading of one machine — the whole of what a collector produces.
/// </summary>
/// <remarks>
/// <para>
/// <b>There is no timestamp on it</b>, which is the single clock made visible:
/// the installation stamps a sample when it arrives, and a field for this
/// machine's clock would be a field somebody eventually trusts
/// (<c>docs/metrics.md</c>).
/// </para>
/// <para>
/// <b>And no host on it either.</b> The token says which machine this is, and
/// there is nothing else for a collector to be told.
/// </para>
/// </remarks>
internal sealed record Reading(
    double Cpu,
    long MemoryUsed,
    long MemoryTotal,
    double Load1,
    double Load5,
    double Load15,
    IReadOnlyList<Filesystem> Filesystems)
{
    /// <summary>
    /// The reading on the wire.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Written member by member rather than serialized off the record's shape.
    /// These names are a contract with an installation that may be newer than
    /// this build (<c>docs/deployment.md</c>), so they are written where they
    /// can be read and compared against <c>docs/metrics.md</c> — and a rename
    /// of a property here cannot quietly become a rename on the wire.
    /// </para>
    /// <para>
    /// It also keeps reflection out of the one program in this repository that
    /// runs on machines nobody here administers, which leaves trimming and
    /// ahead-of-time compilation open as ways to make the image smaller later.
    /// </para>
    /// </remarks>
    public byte[] ToJson()
    {
        var buffer = new ArrayBufferWriter<byte>(256);

        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();

            // Four places. The share is counted off a minute of jiffies, and the
            // digits past these are arithmetic rather than measurement.
            writer.WriteNumber("cpu", Math.Round(Cpu, 4));
            writer.WriteNumber("memoryUsed", MemoryUsed);
            writer.WriteNumber("memoryTotal", MemoryTotal);
            writer.WriteNumber("load1", Load1);
            writer.WriteNumber("load5", Load5);
            writer.WriteNumber("load15", Load15);

            // Always written, even empty. A collector watching no filesystem is
            // an ordinary configuration, and an installation reading an absent
            // member and an empty array the same way is what makes that true on
            // both ends.
            writer.WriteStartArray("filesystems");

            foreach (var filesystem in Filesystems)
            {
                writer.WriteStartObject();
                writer.WriteString("mount", filesystem.Mount);
                writer.WriteNumber("used", filesystem.Used);
                writer.WriteNumber("total", filesystem.Total);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }
}
