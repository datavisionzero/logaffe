using Logaffe.Application.Operations;
using Logaffe.Domain.Operators;

namespace Logaffe.UnitTests.Application;

/// <summary>
/// The list the operator acts on, and the ways a session ends that are not a
/// sign-out.
/// </summary>
public sealed class SessionActsTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 7, 9, 0, 0, TimeSpan.Zero);

    private readonly Guid _operator = Guid.CreateVersion7();
    private readonly InMemorySessions _sessions = new();
    private readonly StoppedClock _clock = new(Now);

    [Fact]
    public async Task The_list_is_what_can_still_act()
    {
        var live = Seed(startedAt: Now.AddDays(-2));
        Seed(startedAt: Now - Session.SlidingLifetime - TimeSpan.FromDays(1));

        var listed = await new ListSessions(_sessions, _clock)
            .ExecuteAsync(TestContext.Current.CancellationToken);

        // An expired row admits nothing — HasExpiredAt is what refuses it — so
        // putting it on the one list the operator reads for a browser that is
        // not theirs would be asking them to recognize a ghost.
        Assert.Equal([live], listed);
    }

    [Fact]
    public async Task Revoking_removes_the_row_and_a_second_attempt_is_not_a_failure()
    {
        var kept = Seed();
        var ended = Seed();
        var revoke = new RevokeSession(_sessions);

        Assert.True(await revoke.ExecuteAsync(ended.Id, TestContext.Current.CancellationToken));
        Assert.Equal([kept], _sessions.Stored);

        // A second click, or another tab. Nothing is wrong and nothing is left
        // to do.
        Assert.False(await revoke.ExecuteAsync(ended.Id, TestContext.Current.CancellationToken));
        Assert.Equal(1, _sessions.Writes);
    }

    [Fact]
    public async Task Ending_every_other_keeps_the_one_asking()
    {
        var asking = Seed();
        Seed();
        Seed();

        await new EndEveryOtherSession(_sessions)
            .ExecuteAsync(asking, TestContext.Current.CancellationToken);

        // Every other, never every one: the browser doing it stays signed in,
        // or securing the installation signs the operator out of the screen they
        // secured it from (docs/sign-in.md).
        Assert.Equal([asking], _sessions.Stored);
    }

    [Fact]
    public async Task The_sweep_removes_what_went_thirty_days_untouched()
    {
        var live = Seed(startedAt: Now.AddDays(-29));
        Seed(startedAt: Now - Session.SlidingLifetime);

        await new RemoveExpiredSessions(_sessions, _clock)
            .ExecuteAsync(TestContext.Current.CancellationToken);

        Assert.Equal([live], _sessions.Stored);
    }

    private Session Seed(DateTimeOffset? startedAt = null) =>
        _sessions.Seed(Session.Start(
            _operator, SessionSecret.Mint(), "203.0.113.7", startedAt ?? Now));
}
