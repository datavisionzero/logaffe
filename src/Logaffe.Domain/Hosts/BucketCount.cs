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

    /// <summary>
    /// The count to use for a range nobody chose one for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One bucket per sample the range can hold, up to <see cref="Maximum"/>.
    /// Slicing an hour into two hundred spans would produce a hundred and forty
    /// empty ones beside sixty carrying a single reading each, which is a band
    /// mostly made of gaps that were never gaps — so the rule is that a bucket
    /// is never finer than the interval that fills it.
    /// </para>
    /// <para>
    /// Rounded down and not up, which is what makes that rule exact rather than
    /// nearly true: a range of an hour and a millisecond rounded up would be
    /// sixty-one spans of a little under a minute each, and a spike would then
    /// be able to fall between two of them.
    /// </para>
    /// <para>
    /// It is here rather than in either adapter because both of them need it and
    /// they must agree: the agent is given no say in it at all
    /// (<c>docs/mcp.md</c>), and the operator's band asks for a number only
    /// because it knows how wide it is on the screen.
    /// </para>
    /// </remarks>
    public static BucketCount For(TimeSpan range)
    {
        if (range <= TimeSpan.Zero)
        {
            return new BucketCount(Minimum);
        }

        var samples = (long)Math.Floor(range / Sampling.Interval);

        return new BucketCount((int)Math.Clamp(samples, Minimum, Maximum));
    }

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
