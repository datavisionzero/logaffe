using Logaffe.Domain.Hosts;

namespace Logaffe.UnitTests.Domain;

/// <summary>
/// How many spans a read of a host's samples is divided into.
/// </summary>
/// <remarks>
/// The rule both consumers share, which is why it is here rather than in either
/// of them: the agent is given no say in it at all, and the operator's band asks
/// for a number only because it knows how wide it is on the screen.
/// </remarks>
public sealed class BucketCountTests
{
    [Theory]
    [InlineData(BucketCount.Minimum)]
    [InlineData(50)]
    [InlineData(BucketCount.Maximum)]
    public void A_count_inside_the_range_is_one(int value) =>
        Assert.Equal(value, BucketCount.Of(value).Value);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(BucketCount.Maximum + 1)]
    public void A_count_outside_it_is_not(int value)
    {
        Assert.False(BucketCount.TryOf(value, out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => BucketCount.Of(value));
    }

    [Fact]
    public void A_range_is_divided_into_one_span_per_sample_it_can_hold()
    {
        // An hour at one reading a minute is sixty, and not the two hundred the
        // ceiling allows: a hundred and forty empty spans beside sixty carrying
        // a single reading each is a band mostly made of gaps that were never
        // gaps.
        Assert.Equal(60, BucketCount.For(TimeSpan.FromHours(1)).Value);
    }

    [Fact]
    public void A_span_is_never_finer_than_the_interval_that_fills_it()
    {
        // Rounded down, which is what makes that exact rather than nearly true.
        // Rounded up, an hour and a millisecond would be sixty-one spans of a
        // little under a minute, and a spike could fall between two of them.
        var range = TimeSpan.FromHours(1) + TimeSpan.FromMilliseconds(1);
        var count = BucketCount.For(range);

        Assert.Equal(60, count.Value);
        Assert.True(range / count.Value >= Sampling.Interval);
    }

    [Fact]
    public void A_range_longer_than_the_ceiling_can_hold_is_divided_into_the_ceiling()
    {
        // A week is ten thousand readings, which would spend an agent's whole
        // context on the shape of a line.
        Assert.Equal(BucketCount.Maximum, BucketCount.For(TimeSpan.FromDays(7)).Value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(30)]
    [InlineData(-60)]
    public void A_range_too_short_to_hold_a_sample_is_still_one_span(int seconds) =>
        Assert.Equal(
            BucketCount.Minimum,
            BucketCount.For(TimeSpan.FromSeconds(seconds)).Value);
}
