using Logaffe.Application.Ports;

namespace Logaffe.IntegrationTests;

/// <summary>
/// What an installation will and will not send with (ADR 0053), which needs no
/// database and lives here because this is the project that can see an adapter.
/// </summary>
/// <remarks>
/// The thing worth proving is the refusals. A half-set mail configuration looks
/// configured in a file and sends nothing, and nobody finds out until somebody
/// is waiting for an invitation — so it stops the start instead.
/// </remarks>
public sealed class SmtpSettingsTests
{
    private const string Origin = "https://logs.example.com";

    [Fact]
    public void An_installation_told_nothing_sends_nothing_and_starts()
    {
        var settings = Read();

        Assert.False(settings.Configured);
        Assert.Null(settings.Host);
        Assert.Null(settings.PublicUrl);
    }

    [Fact]
    public void A_complete_configuration_is_read_with_its_defaults()
    {
        var settings = Read(host: "smtp.example.com", from: "logaffe@example.com",
            publicUrl: Origin);

        Assert.True(settings.Configured);
        Assert.Equal("smtp.example.com", settings.Host);
        Assert.Equal(587, settings.Port);
        Assert.Equal(SmtpSecurity.StartTls, settings.Security);
        Assert.Equal("logaffe@example.com", settings.From);
        Assert.Equal("logaffe", settings.FromName);
        Assert.Equal(new Uri(Origin), settings.PublicUrl);

        // A relay that takes mail from its own network is asked for nothing.
        Assert.Null(settings.Username);
        Assert.Null(settings.Password);
    }

    [Fact]
    public void Half_a_configuration_stops_the_start()
    {
        // The case worth catching: it looks configured and sends nothing.
        Assert.Throws<InvalidOperationException>(
            () => Read(from: "logaffe@example.com", publicUrl: Origin));
    }

    [Fact]
    public void A_host_with_nowhere_to_point_a_link_stops_the_start() =>
        Assert.Throws<InvalidOperationException>(
            () => Read(host: "smtp.example.com", from: "logaffe@example.com"));

    [Fact]
    public void A_username_without_a_password_stops_the_start() =>
        Assert.Throws<InvalidOperationException>(() => Read(
            host: "smtp.example.com", from: "logaffe@example.com", publicUrl: Origin,
            username: "logaffe"));

    [Theory]
    [InlineData("0")]
    [InlineData("70000")]
    [InlineData("not a port")]
    public void A_port_that_is_not_one_stops_the_start(string port) =>
        Assert.Throws<InvalidOperationException>(() => Read(
            host: "smtp.example.com", from: "logaffe@example.com", publicUrl: Origin,
            port: port));

    [Fact]
    public void Something_that_is_not_an_address_to_be_from_stops_the_start() =>
        Assert.Throws<InvalidOperationException>(() => Read(
            host: "smtp.example.com", from: "not an address", publicUrl: Origin));

    [Fact]
    public void An_unencrypted_connection_is_development_only()
    {
        Assert.Throws<InvalidOperationException>(() => Read(
            host: "localhost", from: "logaffe@localhost", publicUrl: Origin,
            security: "none"));

        // Mailpit speaks no TLS, which is the whole of what this allows.
        Assert.Equal(
            SmtpSecurity.None,
            Read(host: "localhost", from: "logaffe@localhost", publicUrl: "http://localhost:5173",
                security: "none", development: true).Security);
    }

    [Theory]
    [InlineData("logs.example.com")]
    [InlineData("https://logs.example.com/")]
    [InlineData("https://logs.example.com/logaffe")]
    [InlineData("https://logs.example.com?a=b")]
    public void An_origin_that_is_not_one_stops_the_start(string publicUrl) =>
        // Links are assembled by appending, so anything else is an address that
        // is subtly wrong in every message rather than obviously wrong once.
        Assert.Throws<InvalidOperationException>(() => Read(publicUrl: publicUrl));

    [Fact]
    public void A_link_sent_by_mail_is_https_outside_development() =>
        Assert.Throws<InvalidOperationException>(() => Read(publicUrl: "http://logs.example.com"));

    private static SmtpSettings Read(
        string? host = null,
        string? port = null,
        string? username = null,
        string? password = null,
        string? security = null,
        string? from = null,
        string? fromName = null,
        string? publicUrl = null,
        bool development = false) =>
        SmtpSettings.Read(
            host, port, username, password, security, from, fromName, publicUrl, development);
}
