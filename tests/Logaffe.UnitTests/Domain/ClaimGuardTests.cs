using Logaffe.Domain.Operators;

namespace Logaffe.UnitTests.Domain;

public sealed class ClaimGuardTests
{
    private static readonly DateTimeOffset FirstRun = new(2026, 8, 7, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void The_window_lasts_thirty_minutes_from_the_first_run()
    {
        var guard = ClaimGuard.OpenedOnFirstRun(FirstRun);

        Assert.Equal(TimeSpan.FromMinutes(30), ClaimGuard.WindowDuration);
        Assert.Equal(FirstRun, guard.OpenedAt);
        Assert.Equal(FirstRun.AddMinutes(30), guard.WindowClosesAt);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(29, true)]
    [InlineData(30, false)]
    [InlineData(31, false)]
    public void It_is_open_until_the_deadline_and_not_at_it(int minutes, bool open)
    {
        var guard = ClaimGuard.OpenedOnFirstRun(FirstRun);

        Assert.Equal(open, guard.WindowIsOpenAt(FirstRun.AddMinutes(minutes)));
    }

    [Fact]
    public void A_fresh_installation_holds_no_drawn_secret()
    {
        var guard = ClaimGuard.OpenedOnFirstRun(FirstRun);

        Assert.False(guard.HasDrawnSecret);
        Assert.Null(guard.DrawnSecretHash);
        Assert.False(guard.AdmitsDrawn(ClaimSecret.Draw()));
    }

    [Fact]
    public void A_drawn_secret_admits_itself_and_nothing_else()
    {
        var guard = ClaimGuard.OpenedOnFirstRun(FirstRun);
        var drawn = ClaimSecret.Draw();

        guard.DrewSecret(drawn);

        Assert.True(guard.HasDrawnSecret);
        Assert.True(guard.AdmitsDrawn(drawn));

        // What the row holds is the hash and not the secret: it is verified
        // against and never read back (ADR 0040).
        Assert.NotNull(guard.DrawnSecretHash);
        Assert.False(guard.AdmitsDrawn(ClaimSecret.Draw()));
    }

    [Fact]
    public void Host_recovery_moves_the_instant_rather_than_making_a_second_guard()
    {
        var guard = ClaimGuard.OpenedOnFirstRun(FirstRun);
        var identity = guard.Id;

        // An hour later, long after the first window lapsed. The operator ran
        // the host command and gets a fresh half hour (ADR 0013).
        var recovery = FirstRun.AddHours(1);
        guard.ArmAt(recovery);

        Assert.Equal(identity, guard.Id);
        Assert.Equal(recovery, guard.OpenedAt);
        Assert.True(guard.WindowIsOpenAt(recovery));
        Assert.False(guard.WindowIsOpenAt(recovery.AddMinutes(30)));
    }

    [Fact]
    public void Host_recovery_forgets_the_secret_that_was_drawn()
    {
        var guard = ClaimGuard.OpenedOnFirstRun(FirstRun);
        var drawn = ClaimSecret.Draw();
        guard.DrewSecret(drawn);

        guard.ArmAt(FirstRun.AddHours(1));

        // This is the moment the installation's notion of who may claim it
        // changes, and a secret that survived it is one the previous operator
        // still holds (ADR 0013).
        Assert.False(guard.HasDrawnSecret);
        Assert.False(guard.AdmitsDrawn(drawn));
    }
}
