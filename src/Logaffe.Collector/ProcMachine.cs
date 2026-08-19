using System.Globalization;

namespace Logaffe.Collector;

/// <summary>
/// What the machine says about itself, less its filesystems.
/// </summary>
internal sealed record MachineReading(
    double Cpu, long MemoryUsed, long MemoryTotal, double Load1, double Load5, double Load15);

/// <summary>
/// The processor, the memory and the load, read where Linux keeps them.
/// </summary>
/// <remarks>
/// <para>
/// A container sees the container, so this reads the <b>host's</b> <c>/proc</c>
/// through the read-only bind mount the handed-over command makes
/// (<c>docs/deployment.md</c>). That is why the path is a setting with a
/// default rather than the literal <c>/proc</c>: outside a container there is
/// no mount and the default is wrong, which is the case a person debugging this
/// is in.
/// </para>
/// <para>
/// <b>It is stateful, and only for the processor.</b> <c>/proc/stat</c> counts
/// ticks since the machine booted, so a share of an interval is a difference
/// between two readings and there is no such thing as one reading of it. The
/// memory and the load are instantaneous and hold nothing.
/// </para>
/// </remarks>
internal sealed class ProcMachine(string procPath)
{
    private Ticks? _previous;

    /// <summary>
    /// The reading, or <c>null</c> when there is no processor share to state
    /// yet — which is true exactly once, before there are two readings of
    /// <c>/proc/stat</c> to put a difference between.
    /// </summary>
    /// <remarks>
    /// Reading a file that is not there throws, and it is left to: a collector
    /// whose <c>/proc</c> mount is missing is misconfigured rather than
    /// unlucky, and <c>Program</c> says so in one line rather than reporting
    /// zeros for a machine it cannot see.
    /// </remarks>
    public MachineReading? Read()
    {
        var ticks = ReadTicks(File.ReadAllText(Path.Combine(procPath, "stat")));
        var previous = _previous;
        _previous = ticks;

        if (previous is null)
        {
            return null;
        }

        var (memoryUsed, memoryTotal) =
            ReadMemory(File.ReadLines(Path.Combine(procPath, "meminfo")));

        var (load1, load5, load15) =
            ReadLoad(File.ReadAllText(Path.Combine(procPath, "loadavg")));

        return new MachineReading(
            Share(previous.Value, ticks), memoryUsed, memoryTotal, load1, load5, load15);
    }

    /// <summary>
    /// The eight counters of the summary line, in the order the kernel writes
    /// them.
    /// </summary>
    /// <param name="Idle">Idle proper, and <paramref name="IoWait"/> beside it.</param>
    internal readonly record struct Ticks(
        ulong User,
        ulong Nice,
        ulong System,
        ulong Idle,
        ulong IoWait,
        ulong Irq,
        ulong SoftIrq,
        ulong Steal)
    {
        public ulong Total => User + Nice + System + Idle + IoWait + Irq + SoftIrq + Steal;

        /// <summary>
        /// Waiting on a disk is not the processor doing something, so it counts
        /// as idle — which is what <c>top</c> does and what makes a machine
        /// hammering a disk read as a machine with a disk problem rather than a
        /// busy one.
        /// </summary>
        public ulong Unused => Idle + IoWait;
    }

    /// <summary>
    /// The share of the interval the machine spent busy.
    /// </summary>
    /// <remarks>
    /// Clamped to the range the installation accepts, because the counters can
    /// go backwards in the one case a collector meets: a machine restored from a
    /// suspend or a container migrated between hosts. A refused delivery would
    /// cost the whole reading, and the honest answer to a negative difference is
    /// that nothing measurable happened.
    /// </remarks>
    internal static double Share(Ticks previous, Ticks current)
    {
        var total = Difference(previous.Total, current.Total);

        if (total == 0)
        {
            return 0;
        }

        var unused = Difference(previous.Unused, current.Unused);

        return Math.Clamp(1 - ((double)unused / total), 0, 1);
    }

    private static ulong Difference(ulong previous, ulong current) =>
        current > previous ? current - previous : 0;

    /// <summary>
    /// The summary line of <c>/proc/stat</c>, which is the first one and is the
    /// only one read: <c>cpu0</c>, <c>cpu1</c> and the rest are the same numbers
    /// split by core, and a machine is one thing here.
    /// </summary>
    /// <remarks>
    /// The last two fields a modern kernel writes — <c>guest</c> and
    /// <c>guest_nice</c> — are deliberately not in the total. They are already
    /// counted inside <c>user</c> and <c>nice</c>, so adding them makes a busy
    /// hypervisor look idle.
    /// </remarks>
    internal static Ticks ReadTicks(string stat)
    {
        foreach (var line in stat.Split('\n'))
        {
            if (!line.StartsWith("cpu ", StringComparison.Ordinal))
            {
                continue;
            }

            var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var read = new ulong[8];

            for (var at = 0; at < read.Length; at++)
            {
                // A kernel older than the field simply stops writing them, and
                // an absent counter is a counter that never moves.
                read[at] = at + 1 < fields.Length && ulong.TryParse(
                    fields[at + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var value)
                    ? value
                    : 0;
            }

            return new Ticks(read[0], read[1], read[2], read[3], read[4], read[5], read[6], read[7]);
        }

        throw new FormatException("There is no cpu line in /proc/stat.");
    }

    /// <summary>
    /// How much of the machine's memory is in use, and how much there is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Used is total less available, and not total less free.</b> Free memory
    /// on a machine that has been up a week is nearly nothing — the kernel has
    /// spent it on cache it will hand back the moment anything asks — so
    /// reporting it would draw every healthy machine at the ceiling and make the
    /// band useless for the one thing it exists to show. <c>MemAvailable</c> is
    /// the kernel's own estimate of what a new allocation could have, which is
    /// the question being asked.
    /// </para>
    /// <para>
    /// <b>It is therefore a larger number than the <c>used</c> column of
    /// <c>free</c></b>, which is worth knowing before comparing the two on one
    /// machine. That column treats every page of cache as free; this treats the
    /// part of it the kernel says it could not hand back as used, which is the
    /// difference between *what the processes asked for* and *how close this
    /// machine is to not being able to give it*. The filesystem numbers beside
    /// it do agree with <c>df</c>, and this one deliberately does not agree with
    /// <c>free</c>.
    /// </para>
    /// </remarks>
    internal static (long Used, long Total) ReadMemory(IEnumerable<string> meminfo)
    {
        long total = 0;
        long available = 0;
        long free = 0;

        foreach (var line in meminfo)
        {
            if (TryKilobytes(line, "MemTotal:", out var read))
            {
                total = read;
            }
            else if (TryKilobytes(line, "MemAvailable:", out read))
            {
                available = read;
            }
            else if (TryKilobytes(line, "MemFree:", out read))
            {
                free = read;
            }
        }

        // Kernels before 3.14 do not write `MemAvailable`. Free is the worse
        // answer and it is the only other one there is.
        var spare = available > 0 ? available : free;

        return (Math.Max(total - spare, 0), total);
    }

    /// <summary>
    /// The one-, five- and fifteen-minute averages, which are the first three
    /// fields of <c>/proc/loadavg</c>. The two after them count processes, and
    /// the schema has no room for them (ADR 0044).
    /// </summary>
    internal static (double One, double Five, double Fifteen) ReadLoad(string loadavg)
    {
        var fields = loadavg.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return (Number(fields, 0), Number(fields, 1), Number(fields, 2));
    }

    private static double Number(string[] fields, int at) =>
        at < fields.Length && double.TryParse(
            fields[at], NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
        && double.IsFinite(value)
        && value >= 0
            ? value
            : 0;

    /// <summary>
    /// A <c>/proc/meminfo</c> line, which is a name, a number and the unit
    /// <c>kB</c> — meaning 1024 bytes, whatever the letter suggests.
    /// </summary>
    private static bool TryKilobytes(string line, string name, out long bytes)
    {
        bytes = 0;

        if (!line.StartsWith(name, StringComparison.Ordinal))
        {
            return false;
        }

        var fields = line[name.Length..]
            .Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        if (fields.Length == 0 || !long.TryParse(
            fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out var kilobytes))
        {
            return false;
        }

        bytes = kilobytes * 1024;
        return true;
    }
}
