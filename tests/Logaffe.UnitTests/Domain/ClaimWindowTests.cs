using Logaffe.Domain.Operators;

namespace Logaffe.UnitTests.Domain;

public sealed class ClaimWindowTests
{
    private static readonly DateTimeOffset FirstRun = new(2026, 8, 7, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void The_window_lasts_thirty_minutes_from_the_first_run()
    {
        var window = ClaimWindow.OpenedOnFirstRun(FirstRun);

        Assert.Equal(TimeSpan.FromMinutes(30), ClaimWindow.Duration);
        Assert.Equal(FirstRun, window.OpenedAt);
        Assert.Equal(FirstRun.AddMinutes(30), window.ClosesAt);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(29, true)]
    [InlineData(30, false)]
    [InlineData(31, false)]
    public void It_is_open_until_the_deadline_and_not_at_it(int minutes, bool open)
    {
        var window = ClaimWindow.OpenedOnFirstRun(FirstRun);

        Assert.Equal(open, window.IsOpenAt(FirstRun.AddMinutes(minutes)));
    }

    [Fact]
    public void Host_recovery_moves_the_instant_rather_than_making_a_second_window()
    {
        var window = ClaimWindow.OpenedOnFirstRun(FirstRun);
        var identity = window.Id;

        // An hour later, long after the first window lapsed. The operator ran
        // the host command and gets a fresh half hour (ADR 0013).
        var recovery = FirstRun.AddHours(1);
        window.ArmAt(recovery);

        Assert.Equal(identity, window.Id);
        Assert.Equal(recovery, window.OpenedAt);
        Assert.True(window.IsOpenAt(recovery));
        Assert.False(window.IsOpenAt(recovery.AddMinutes(30)));
    }
}
