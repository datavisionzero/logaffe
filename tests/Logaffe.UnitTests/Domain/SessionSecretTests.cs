using Logaffe.Domain.Operators;

namespace Logaffe.UnitTests.Domain;

public sealed class SessionSecretTests
{
    [Fact]
    public void A_minted_secret_survives_the_trip_to_a_cookie_and_back()
    {
        var secret = SessionSecret.Mint();

        Assert.True(SessionSecret.TryParse(secret.Text, out var presented));
        Assert.Equal(secret.Text, presented.Text);
        // The hash is what the row holds, so this is the whole of what makes a
        // returning browser the same session.
        Assert.Equal(secret.Hash, presented.Hash);
    }

    [Fact]
    public void Two_secrets_are_two_secrets()
    {
        Assert.NotEqual(SessionSecret.Mint().Text, SessionSecret.Mint().Text);
        Assert.NotEqual(SessionSecret.Mint().Hash, SessionSecret.Mint().Hash);
    }

    [Fact]
    public void A_secret_travels_without_escaping() =>
        // base64url, so a cookie carries it as it is.
        Assert.True(SessionSecret.Mint().Text.All(
            character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_'));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a session secret")]
    // Base64url of thirty-one bytes, and of thirty-three: the right alphabet and
    // the wrong size is still not one of these.
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public void What_is_not_a_secret_is_refused_before_any_session_is_fetched(string? presented) =>
        Assert.False(SessionSecret.TryParse(presented, out _));

    [Fact]
    public void A_secret_carries_nothing_into_a_log_line()
    {
        var secret = SessionSecret.Mint();

        Assert.DoesNotContain(secret.Text, secret.ToString());
    }
}
