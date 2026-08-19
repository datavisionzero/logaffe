namespace Logaffe.Domain.Hosts;

/// <summary>
/// One reading a collector took of its host: what the machine was doing in the
/// minute this covers.
/// </summary>
/// <remarks>
/// <para>
/// It is written once and never edited, and it leaves only by ageing out,
/// exactly as a log entry does. <c>docs/storage.md</c> is the table this shape
/// is, column for column.
/// </para>
/// <para>
/// <b>It carries one clock, and it is the installation's.</b> An entry has two
/// because a sender's clock can be wrong about when something happened and
/// retention may not count from a number the sender chose (ADR 0007). A sample
/// has nothing to bridge: delivery is fire-and-forget with no buffer and no
/// retry, so a reading is at most a second old when it lands, and nothing asks
/// for samples in an order other than the one they arrived in. What the single
/// clock removes is a collector whose clock is a year fast writing samples the
/// retention sweep will never reach.
/// </para>
/// <para>
/// <b>The bounds here are the whole of what a reading may be.</b> Unlike an
/// entry, which is truncated rather than refused because the entries that
/// overrun a cap are the ones the operator went looking for (ADR 0008), a number
/// outside its range is not a large reading — it is not a reading. It is refused,
/// and the next one is a minute away.
/// </para>
/// </remarks>
public sealed class Sample
{
    private readonly double _cpu;
    private readonly long _memoryUsed;
    private readonly long _memoryTotal;
    private readonly double _load1;
    private readonly double _load5;
    private readonly double _load15;

    /// <summary>
    /// The host this was read off, by the identity that survives its rename.
    /// </summary>
    /// <remarks>
    /// It leads the key, because every read of this table names one host and a
    /// key that did not lead with it would make each of them pay for every other
    /// machine's minutes.
    /// </remarks>
    public required Guid HostId { get; init; }

    /// <summary>
    /// When the installation received this, which is also within a second of
    /// when the machine was read. It is what retention counts from and what a
    /// range is asked over, and together with <see cref="HostId"/> it is the
    /// key — so a host reporting twice for one minute is a conflict rather than
    /// a second row that quietly doubles a machine on the band.
    /// </summary>
    public required DateTimeOffset ReceiptTime { get; init; }

    /// <summary>
    /// The share of the interval the machine spent busy, from nought to one.
    /// </summary>
    public required double Cpu
    {
        get => _cpu;
        init => _cpu = InRange(value, 0, 1, nameof(Cpu));
    }

    /// <summary>Memory in use, in bytes.</summary>
    public required long MemoryUsed
    {
        get => _memoryUsed;
        init => _memoryUsed = NotNegative(value, nameof(MemoryUsed));
    }

    /// <summary>
    /// Memory the machine has, in bytes. It is stored beside the used figure
    /// rather than a percentage being computed on the way in, because how full a
    /// machine is and how large it is answer different questions and only one of
    /// them survives the division.
    /// </summary>
    public required long MemoryTotal
    {
        get => _memoryTotal;
        init => _memoryTotal = NotNegative(value, nameof(MemoryTotal));
    }

    /// <summary>The one-minute load average.</summary>
    public required double Load1
    {
        get => _load1;
        init => _load1 = NotNegative(value, nameof(Load1));
    }

    /// <summary>The five-minute load average.</summary>
    public required double Load5
    {
        get => _load5;
        init => _load5 = NotNegative(value, nameof(Load5));
    }

    /// <summary>The fifteen-minute load average.</summary>
    public required double Load15
    {
        get => _load15;
        init => _load15 = NotNegative(value, nameof(Load15));
    }

    private static double InRange(double value, double low, double high, string name) =>
        double.IsFinite(value) && value >= low && value <= high
            ? value
            : throw new ArgumentOutOfRangeException(
                name, value, $"A sample's {name} is between {low} and {high}.");

    private static double NotNegative(double value, string name) =>
        double.IsFinite(value) && value >= 0
            ? value
            : throw new ArgumentOutOfRangeException(
                name, value, $"A sample's {name} is not negative.");

    private static long NotNegative(long value, string name) =>
        value >= 0
            ? value
            : throw new ArgumentOutOfRangeException(
                name, value, $"A sample's {name} is not negative.");
}
