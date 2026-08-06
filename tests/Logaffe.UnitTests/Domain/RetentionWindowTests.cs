using Logaffe.Domain.Projects;

namespace Logaffe.UnitTests.Domain;

public sealed class RetentionWindowTests
{
    [Fact]
    public void The_ceiling_is_ninety_days()
    {
        Assert.Equal(90, RetentionWindow.MaximumDays);
        Assert.True(RetentionWindow.TryOfDays(90, out _));
        Assert.False(RetentionWindow.TryOfDays(91, out _));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_window_is_at_least_a_day(int days) =>
        Assert.False(RetentionWindow.TryOfDays(days, out _));

    [Fact]
    public void No_installation_can_raise_the_ceiling() =>
        // The assumptions the rest of the product rests on — index sizes, the
        // volume storage is tuned for — stop being true above this.
        Assert.Throws<ArgumentOutOfRangeException>(() => RetentionWindow.OfDays(365));

    [Fact]
    public void Two_windows_of_the_same_length_are_the_same_window() =>
        Assert.Equal(RetentionWindow.OfDays(7), RetentionWindow.OfDays(7));

    [Fact]
    public void It_carries_its_duration() =>
        Assert.Equal(TimeSpan.FromDays(7), RetentionWindow.OfDays(7).Duration);
}
