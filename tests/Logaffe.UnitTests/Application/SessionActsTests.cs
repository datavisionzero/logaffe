using Logaffe.Application.Operations;
using Logaffe.Domain.Identities;

namespace Logaffe.UnitTests.Application;

/// <summary>
/// The list a user acts on, which is their own, and the ways a session ends that
/// are not a sign-out.
/// </summary>
public sealed class SessionActsTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 7, 9, 0, 0, TimeSpan.Zero);

    private readonly Guid _user = Guid.CreateVersion7();
    private readonly Guid _somebodyElse = Guid.CreateVersion7();
    private readonly InMemorySessions _sessions = new();
    private readonly StoppedClock _clock = new(Now);

    [Fact]
    public async Task The_list_is_what_can_still_act()
    {
        var live = Seed(startedAt: Now.AddDays(-2));
        Seed(startedAt: Now - Session.IdleLifetime - TimeSpan.FromDays(1));
        Seed(owner: _somebodyElse);

        var listed = await new ListSessions(_sessions, _clock)
            .ExecuteAsync(_user, TestContext.Current.CancellationToken);

        // An expired row admits nothing — HasExpiredAt is what refuses it — so
        // putting it on the one list somebody reads for a browser that is not
        // theirs would be asking them to recognize a ghost. Nor is anybody
        // else's row on it (ADR 0055).
        Assert.Equal([live], listed);
    }

    [Fact]
    public async Task Revoking_removes_the_row_and_a_second_attempt_is_not_a_failure()
    {
        var kept = Seed();
        var ended = Seed();
        var revoke = new RevokeSession(_sessions);

        Assert.True(await revoke.ExecuteAsync(
            _user, ended.Id, TestContext.Current.CancellationToken));
        Assert.Equal([kept], _sessions.Stored);

        // A second click, or another tab. Nothing is wrong and nothing is left
        // to do.
        Assert.False(await revoke.ExecuteAsync(
            _user, ended.Id, TestContext.Current.CancellationToken));
        Assert.Equal(1, _sessions.Writes);
    }

    [Fact]
    public async Task Somebody_else_s_session_is_not_found_by_its_id()
    {
        var theirs = Seed(owner: _somebodyElse);

        // Not a refusal that says the row is out there under another name: the
        // act looks in the caller's own list and finds nothing (ADR 0055).
        Assert.False(await new RevokeSession(_sessions)
            .ExecuteAsync(_user, theirs.Id, TestContext.Current.CancellationToken));
        Assert.Equal([theirs], _sessions.Stored);
    }

    [Fact]
    public async Task Ending_every_other_keeps_the_one_asking_and_nobody_else_s()
    {
        var asking = Seed();
        Seed();
        Seed();
        var theirs = Seed(owner: _somebodyElse);

        await new EndEveryOtherSession(_sessions)
            .ExecuteAsync(asking, TestContext.Current.CancellationToken);

        // Every other of this user's, never every one and never anybody else's:
        // the browser doing it stays signed in, or securing the account signs
        // somebody out of the screen they secured it from (docs/sign-in.md).
        Assert.Equal([asking, theirs], _sessions.Stored);
    }

    [Fact]
    public async Task The_sweep_removes_what_went_past_either_deadline()
    {
        var live = Seed(startedAt: Now.AddDays(-6));
        Seed(startedAt: Now - Session.IdleLifetime);
        Seed(startedAt: Now - Session.AbsoluteLifetime);

        await new RemoveExpiredSessions(_sessions, _clock)
            .ExecuteAsync(TestContext.Current.CancellationToken);

        Assert.Equal([live], _sessions.Stored);
    }

    private Session Seed(DateTimeOffset? startedAt = null, Guid? owner = null) =>
        _sessions.Seed(Session.Start(
            owner ?? _user, SessionSecret.Mint(), "203.0.113.7", startedAt ?? Now));
}
