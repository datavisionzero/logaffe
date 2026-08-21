using Logaffe.Domain.Projects;

namespace Logaffe.UnitTests.Domain;

public sealed class RetentionWindowTests
{
    [Fact]
    public void The_ceiling_is_a_year()
    {
        Assert.Equal(365, RetentionWindow.MaximumDays);
        Assert.True(RetentionWindow.TryOfDays(365, out _));
        Assert.False(RetentionWindow.TryOfDays(366, out _));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_window_is_at_least_a_day(int days) =>
        Assert.False(RetentionWindow.TryOfDays(days, out _));

    [Fact]
    public void No_installation_can_raise_the_ceiling() =>
        // A year is where this product ends and a different one begins, and
        // moving the line is a change to ADR 0048 rather than a setting. What
        // stands between the operator and a window they cannot afford below it
        // is the footprint they are shown, not this.
        Assert.Throws<ArgumentOutOfRangeException>(() => RetentionWindow.OfDays(366));

    [Fact]
    public void Two_windows_of_the_same_length_are_the_same_window() =>
        Assert.Equal(RetentionWindow.OfDays(7), RetentionWindow.OfDays(7));

    [Fact]
    public void It_carries_its_duration() =>
        Assert.Equal(TimeSpan.FromDays(7), RetentionWindow.OfDays(7).Duration);
}
