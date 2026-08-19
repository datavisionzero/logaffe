namespace Logaffe.Domain.Hosts;

/// <summary>
/// How many spans a read of a host's samples is divided into.
/// </summary>
/// <remarks>
/// A week at one sample a minute is ten thousand readings, which would spend an
/// agent's context on the shape of a line and give a band far more points than
/// it has pixels. Bucketing is therefore not an option a caller may decline —
/// what it chooses is how many, and the ceiling is what makes the answer's size
/// a property of the product rather than of the range asked for.
/// </remarks>
public sealed record BucketCount
{
    public const int Minimum = 1;

    /// <summary>
    /// The same two hundred the compact search caps its entries at
    /// (<c>docs/mcp.md</c>), for the same reason and to save anyone reading both
    /// from wondering whether the difference means something.
    /// </summary>
    public const int Maximum = 200;

    private BucketCount(int value) => Value = value;

    public int Value { get; }

    public static BucketCount Of(int value) =>
        TryOf(value, out var count)
            ? count
            : throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                $"A read is divided into between {Minimum} and {Maximum} buckets.");

    public static bool TryOf(int value, out BucketCount count)
    {
        if (value is < Minimum or > Maximum)
        {
            count = null!;
            return false;
        }

        count = new BucketCount(value);
        return true;
    }

    public override string ToString() => Value.ToString();
}
