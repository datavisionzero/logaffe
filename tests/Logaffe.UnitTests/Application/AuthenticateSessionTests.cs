using Logaffe.Application.Operations;
using Logaffe.Domain.Operators;

namespace Logaffe.UnitTests.Application;

public sealed class AuthenticateSessionTests
{
    private static readonly DateTimeOffset Started = new(2026, 8, 7, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid TheOperator = Guid.CreateVersion7();

    [Fact]
    public async Task A_secret_admits_the_session_it_belongs_to()
    {
        var (sessions, secret, session) = Signed_in_browser();
        var clock = new StoppedClock(Started);

        var admitted = await new AuthenticateSession(sessions, clock).ExecuteAsync(
            secret.Text, "203.0.113.7", TestContext.Current.CancellationToken);

        Assert.NotNull(admitted);
        Assert.Same(session, admitted.Session);
    }

    [Fact]
    public async Task A_secret_is_found_wherever_in_the_list_it_sits()
    {
        // There is no identifier naming a row here the way there is on a token
        // (ADR 0031): one account holds a handful of sessions and the presented
        // value is compared against all of them.
        var sessions = new InMemorySessions();

        var wanted = SessionSecret.Mint();
        sessions.Seed(Session.Start(TheOperator, wanted, "203.0.113.7", Started));

        for (var other = 0; other < 4; other++)
        {
            sessions.Seed(
                Session.Start(TheOperator, SessionSecret.Mint(), "198.51.100.4", Started));
        }

        Assert.NotNull(await new AuthenticateSession(sessions, new StoppedClock(Started))
            .ExecuteAsync(wanted.Text, null, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Something_that_is_not_a_secret_admits_nothing_and_reads_nothing()
    {
        var (sessions, _, _) = Signed_in_browser();
        var authenticate = new AuthenticateSession(sessions, new StoppedClock(Started));

        foreach (var presented in new[] { null, string.Empty, "not-a-session-secret", "!!!" })
        {
            Assert.Null(await authenticate.ExecuteAsync(
                presented, null, TestContext.Current.CancellationToken));
        }

        // The wrong length, or a character outside base64url, is refused before
        // the table is touched at all.
        Assert.Equal(0, sessions.Reads);
    }

    [Fact]
    public async Task A_secret_that_is_nobody_s_admits_nothing()
    {
        var (sessions, _, _) = Signed_in_browser();

        Assert.Null(await new AuthenticateSession(sessions, new StoppedClock(Started))
            .ExecuteAsync(
                SessionSecret.Mint().Text, null, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_session_left_untouched_past_its_deadline_admits_nothing()
    {
        var (sessions, secret, _) = Signed_in_browser();
        var lapsed = new StoppedClock(Started + Session.SlidingLifetime);

        Assert.Null(await new AuthenticateSession(sessions, lapsed).ExecuteAsync(
            secret.Text, null, TestContext.Current.CancellationToken));

        // The row is left where it is: removing the ones nobody touched is
        // housekeeping with its own sweep, and it is not what refused this.
        Assert.Single(sessions.Stored);
        Assert.Equal(0, sessions.Writes);
    }

    [Fact]
    public async Task A_use_inside_the_interval_writes_nothing()
    {
        var (sessions, secret, session) = Signed_in_browser();
        var clock = new StoppedClock(
            Started + AuthenticateSession.UseWriteInterval - TimeSpan.FromSeconds(1));

        var admitted = await new AuthenticateSession(sessions, clock).ExecuteAsync(
            secret.Text, "198.51.100.4", TestContext.Current.CancellationToken);

        // ADR 0033's reasoning, applied to the row a live-tailing log view
        // touches every few seconds.
        Assert.NotNull(admitted);
        Assert.False(admitted.DeadlineMoved);
        Assert.Equal(0, sessions.Writes);
        Assert.Equal(Started, session.LastUsedAt);
    }

    [Fact]
    public async Task A_use_past_the_interval_moves_the_deadline_and_the_address()
    {
        var (sessions, secret, session) = Signed_in_browser();
        var later = Started + AuthenticateSession.UseWriteInterval;
        var clock = new StoppedClock(later);

        var admitted = await new AuthenticateSession(sessions, clock).ExecuteAsync(
            secret.Text, "198.51.100.4", TestContext.Current.CancellationToken);

        Assert.NotNull(admitted);
        Assert.True(admitted.DeadlineMoved);
        Assert.Equal(1, sessions.Writes);
        Assert.Equal(later, session.LastUsedAt);
        Assert.Equal(later + Session.SlidingLifetime, session.ExpiresAt);

        // The column the operator reads for anything unfamiliar, accurate to
        // within the same five minutes and not to be shown as finer.
        Assert.Equal("198.51.100.4", session.LastSeenFrom);
    }

    [Fact]
    public async Task An_address_the_product_could_not_read_is_a_word_rather_than_a_blank()
    {
        var sessions = new InMemorySessions();
        var secret = SessionSecret.Mint();
        sessions.Seed(Session.Start(TheOperator, secret, null, Started));

        var admitted = await new AuthenticateSession(sessions, new StoppedClock(Started))
            .ExecuteAsync(secret.Text, null, TestContext.Current.CancellationToken);

        Assert.NotNull(admitted);
        Assert.Equal("unknown", admitted.Session.LastSeenFrom);
    }

    private static (InMemorySessions Sessions, SessionSecret Secret, Session Session)
        Signed_in_browser()
    {
        var sessions = new InMemorySessions();
        var secret = SessionSecret.Mint();

        return (
            sessions,
            secret,
            sessions.Seed(Session.Start(TheOperator, secret, "203.0.113.7", Started)));
    }
}
