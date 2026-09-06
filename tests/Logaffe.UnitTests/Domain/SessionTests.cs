using Logaffe.Domain.Identities;

namespace Logaffe.UnitTests.Domain;

public sealed class SessionTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_started_session_has_just_been_used()
    {
        var userId = Guid.CreateVersion7();
        var secret = SessionSecret.Mint();

        var session = Session.Start(userId, secret, "203.0.113.7", Now);

        Assert.NotEqual(Guid.Empty, session.Id);
        Assert.Equal(userId, session.UserId);
        Assert.Equal(secret.Hash, session.SecretHash);
        Assert.Equal("203.0.113.7", session.LastSeenFrom);
        Assert.Equal(Now, session.StartedAt);
        Assert.Equal(Now, session.LastUsedAt);
    }

    [Fact]
    public void A_session_lasts_seven_days_from_its_last_use()
    {
        var session = Session.Start(Guid.CreateVersion7(), SessionSecret.Mint(), "203.0.113.7", Now);

        Assert.Equal(Now.AddDays(7), session.ExpiresAt);
        Assert.False(session.HasExpiredAt(Now.AddDays(7).AddTicks(-1)));
        Assert.True(session.HasExpiredAt(Now.AddDays(7)));
    }

    [Fact]
    public void Every_use_pushes_the_idle_deadline_forward()
    {
        var session = Session.Start(Guid.CreateVersion7(), SessionSecret.Mint(), "203.0.113.7", Now);

        session.WasUsedAt(Now.AddDays(5), "198.51.100.4");

        // An installation somebody works in daily is not a place where they keep
        // re-authenticating.
        Assert.Equal(Now.AddDays(12), session.ExpiresAt);
        Assert.Equal("198.51.100.4", session.LastSeenFrom);
    }

    [Fact]
    public void Nothing_pushes_the_absolute_deadline()
    {
        var session = Session.Start(Guid.CreateVersion7(), SessionSecret.Mint(), "203.0.113.7", Now);

        // Used every day for a month, which is what a browser somebody works in
        // looks like.
        for (var day = 1; day <= 29; day++)
        {
            session.WasUsedAt(Now.AddDays(day), "203.0.113.7");
        }

        // Thirty days from the sign-in, whatever happened in between: a cookie
        // somebody took must not be permanent as long as it is used.
        Assert.Equal(Now.AddDays(30), session.ExpiresAt);
        Assert.True(session.HasExpiredAt(Now.AddDays(30)));
    }

    [Fact]
    public void A_use_that_arrives_late_does_not_make_a_session_look_older()
    {
        var session = Session.Start(Guid.CreateVersion7(), SessionSecret.Mint(), "203.0.113.7", Now);
        session.WasUsedAt(Now.AddHours(2), "198.51.100.4");

        session.WasUsedAt(Now.AddHours(1), "192.0.2.9");

        // Two requests out of order must not move the last use backwards, and
        // must not make the list show where the older one came from.
        Assert.Equal(Now.AddHours(2), session.LastUsedAt);
        Assert.Equal("198.51.100.4", session.LastSeenFrom);
    }

    [Fact]
    public void A_session_the_product_could_not_place_says_so()
    {
        var session = Session.Start(Guid.CreateVersion7(), SessionSecret.Mint(), null, Now);

        // The list has to say something in that column: a blank one reads as a
        // bug in the row rather than as a fact about the request.
        Assert.Equal("unknown", session.LastSeenFrom);
    }

    [Fact]
    public void An_address_that_will_not_fit_the_column_is_cut_rather_than_refused()
    {
        var session = Session.Start(
            Guid.CreateVersion7(), SessionSecret.Mint(), new string('x', 200), Now);

        Assert.Equal(Session.SeenFromMaxLength, session.LastSeenFrom.Length);
    }

    [Fact]
    public void A_session_is_its_secret_and_nothing_else()
    {
        var secret = SessionSecret.Mint();
        var session = Session.Start(Guid.CreateVersion7(), secret, "203.0.113.7", Now);

        Assert.True(session.Matches(secret));
        Assert.True(SessionSecret.TryParse(secret.Text, out var presented));
        Assert.True(session.Matches(presented));
        Assert.False(session.Matches(SessionSecret.Mint()));
    }
}
