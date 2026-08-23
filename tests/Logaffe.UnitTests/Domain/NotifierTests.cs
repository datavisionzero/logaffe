using Logaffe.Domain.Alerts;

namespace Logaffe.UnitTests.Domain;

public sealed class NotifierTests
{
    [Theory]
    [InlineData("https://ntfy.sh")]
    [InlineData("http://ntfy.internal:8080")]
    [InlineData("https://example.com/ntfy")]
    public void A_server_and_a_topic_make_a_notifier(string server) =>
        Assert.True(Notifier.TryCreate(server, "logaffe", null, out _));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ntfy.sh")]
    [InlineData("ftp://ntfy.sh")]
    [InlineData("/ntfy")]
    [InlineData("https://ntfy.sh?topic=logaffe")]
    [InlineData("https://ntfy.sh#logaffe")]
    [InlineData("https://user:pass@ntfy.sh")]
    public void What_is_not_an_address_this_will_post_to_is_refused(string? server) =>
        Assert.False(Notifier.TryCreate(server, "logaffe", null, out _));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("two words")]
    [InlineData("logaffe/alerts")]
    [InlineData("logaffe?x=1")]
    [InlineData("../../etc")]
    public void What_is_not_a_topic_is_refused(string? topic) =>
        Assert.False(Notifier.TryCreate("https://ntfy.sh", topic, null, out _));

    [Fact]
    public void A_topic_longer_than_ntfy_takes_is_refused() =>
        Assert.False(Notifier.TryCreate(
            "https://ntfy.sh", new string('t', Notifier.TopicMaxLength + 1), null, out _));

    [Fact]
    public void Both_are_trimmed() =>
        Assert.Equal(
            "logaffe", Notifier.Create("  https://ntfy.sh  ", "  logaffe  ", null).Topic);

    /// <summary>
    /// The reason the server is kept with a trailing slash: an ntfy behind a
    /// proxy is commonly under a path, and resolving a topic against an address
    /// without one replaces the last segment instead of adding to it.
    /// </summary>
    [Fact]
    public void A_server_under_a_path_keeps_it_when_the_topic_is_appended() =>
        Assert.Equal(
            "https://example.com/ntfy/logaffe",
            Notifier.Create("https://example.com/ntfy", "logaffe", null).Endpoint.ToString());

    [Fact]
    public void A_server_at_the_root_addresses_its_topic() =>
        Assert.Equal(
            "https://ntfy.sh/logaffe",
            Notifier.Create("https://ntfy.sh", "logaffe", null).Endpoint.ToString());

    /// <summary>
    /// The token is held as the row holds it, and this type has no way to open
    /// one (ADR 0022).
    /// </summary>
    [Fact]
    public void The_token_is_carried_sealed()
    {
        var sealedToken = new byte[] { 1, 2, 3 };

        Assert.Same(
            sealedToken,
            Notifier.Create("https://ntfy.sh", "logaffe", null)
                .Sealing(sealedToken)
                .EncryptedAccessToken);
    }

    [Fact]
    public void A_public_topic_carries_none() =>
        Assert.Null(Notifier.Create("https://ntfy.sh", "logaffe", null).EncryptedAccessToken);
}
