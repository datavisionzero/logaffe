using System.Net.Mail;

namespace Logaffe.Application.Ports;

/// <summary>How the connection to the mail server is secured.</summary>
public enum SmtpSecurity
{
    /// <summary>Connect in the clear and upgrade, which is what port 587 means.</summary>
    StartTls,

    /// <summary>TLS from the first byte, which is what port 465 means.</summary>
    Tls,

    /// <summary>
    /// Neither, which is only ever Mailpit on a development machine and is
    /// refused anywhere else.
    /// </summary>
    None,
}

/// <summary>
/// The SMTP account an installation sends through, and the address its links are
/// built from (ADR 0053).
/// </summary>
/// <remarks>
/// <para>
/// <b>It is configuration and not a second service.</b> Whoever installs supplies
/// a host, a port, a transport-security mode, credentials, a sender and a public
/// base URL; there is no queue, no retry engine and no third production
/// container.
/// </para>
/// <para>
/// <b>It is read once, at startup, and a value the installation will not accept
/// stops the start.</b> A mail configuration that is wrong is a configuration
/// nobody finds out about until somebody is waiting for an invitation, which is
/// the worst moment for it to surface.
/// </para>
/// </remarks>
public sealed record SmtpSettings(
    string? Host,
    int Port,
    string? Username,
    string? Password,
    SmtpSecurity Security,
    string? From,
    string FromName,
    Uri? PublicUrl)
{
    public const string HostKey = "Logaffe:Smtp:Host";
    public const string PortKey = "Logaffe:Smtp:Port";
    public const string UsernameKey = "Logaffe:Smtp:Username";
    public const string PasswordKey = "Logaffe:Smtp:Password";
    public const string SecurityKey = "Logaffe:Smtp:Security";
    public const string FromKey = "Logaffe:Smtp:From";
    public const string FromNameKey = "Logaffe:Smtp:FromName";

    /// <summary>
    /// Where this installation is reached, which the alerts already use
    /// (<c>docs/alerts.md</c>, <c>AlertLinks</c>) and which every link in a
    /// message is built from. One setting for both, because an installation has
    /// one address — and the same setting rather than a second one, so that the
    /// link in an invitation and the link in an alert cannot disagree.
    /// </summary>
    public const string PublicUrlKey = "Logaffe:PublicUrl";

    /// <summary>What an installation that was told nothing runs with.</summary>
    public static SmtpSettings None { get; } =
        new(null, 587, null, null, SmtpSecurity.StartTls, null, "logaffe", null);

    /// <summary>
    /// Whether this installation can send. Everything else about mail is
    /// downstream of this one fact (ADR 0053).
    /// </summary>
    public bool Configured => Host is not null && PublicUrl is not null;

    /// <summary>
    /// Reads the settings, and refuses the ones an installation cannot serve on.
    /// </summary>
    /// <param name="development">
    /// Whether this is a development installation, which is the one place an
    /// unencrypted connection and a plain <c>http</c> address are allowed —
    /// Mailpit speaks neither TLS nor HTTPS and belongs nowhere else
    /// (ADR 0053).
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// Something is set that this installation will not send with. It is a
    /// sentence naming the key, because the person reading it has the compose
    /// file open.
    /// </exception>
    public static SmtpSettings Read(
        string? host,
        string? port,
        string? username,
        string? password,
        string? security,
        string? from,
        string? fromName,
        string? publicUrl,
        bool development)
    {
        var url = ReadPublicUrl(publicUrl, development);
        var named = !string.IsNullOrWhiteSpace(host);

        // Half a mail configuration is the case worth catching: it looks
        // configured in a file and sends nothing, and nobody finds out until
        // somebody is waiting for an invitation.
        if (!named
            && new[] { port, username, password, security, from }
                .Any(value => !string.IsNullOrWhiteSpace(value)))
        {
            throw new InvalidOperationException(
                $"{HostKey} is not set and another {nameof(SmtpSettings)} key is. Set the "
                + "host, or clear the rest: an installation sends through one server or "
                + "through none. See docs/setup.md.");
        }

        if (!named)
        {
            return None with { PublicUrl = url, FromName = Named(fromName) };
        }

        if (url is null)
        {
            throw new InvalidOperationException(
                $"{HostKey} is set and {PublicUrlKey} is not. Every link in a message is "
                + "built from that address and never from an incoming Host header, so "
                + "there is nothing to put in one. See docs/setup.md.");
        }

        return new(
            host!.Trim(),
            ReadPort(port),
            string.IsNullOrWhiteSpace(username) ? null : username.Trim(),
            ReadPassword(username, password),
            ReadSecurity(security, development),
            ReadFrom(from),
            Named(fromName),
            url);
    }

    private static string Named(string? fromName) =>
        string.IsNullOrWhiteSpace(fromName) ? "logaffe" : fromName.Trim();

    private static int ReadPort(string? port) =>
        string.IsNullOrWhiteSpace(port)
            ? 587
            : int.TryParse(port, out var parsed) && parsed is > 0 and <= 65535
                ? parsed
                : throw new InvalidOperationException(
                    $"{PortKey} is '{port}', which is not a port from 1 through 65535.");

    private static SmtpSecurity ReadSecurity(string? security, bool development) =>
        (string.IsNullOrWhiteSpace(security) ? "starttls" : security.Trim().ToLowerInvariant())
        switch
        {
            "starttls" => SmtpSecurity.StartTls,
            "tls" => SmtpSecurity.Tls,
            "none" when development => SmtpSecurity.None,
            "none" => throw new InvalidOperationException(
                $"{SecurityKey} is `none`, which sends credentials and addresses in the "
                + "clear. It is for Mailpit on a development machine and nothing else."),
            _ => throw new InvalidOperationException(
                $"{SecurityKey} is '{security}', which is none of `starttls`, `tls` or "
                + "`none`."),
        };

    /// <remarks>
    /// The two go together: a username with no password authenticates nothing,
    /// and a password with no username is a secret in a file that reaches
    /// nowhere.
    /// </remarks>
    private static string? ReadPassword(string? username, string? password)
    {
        var hasUsername = !string.IsNullOrWhiteSpace(username);
        var hasPassword = !string.IsNullOrWhiteSpace(password);

        return hasUsername == hasPassword
            ? hasPassword ? password : null
            : throw new InvalidOperationException(
                $"{UsernameKey} and {PasswordKey} go together, and only one of them is "
                + "set.");
    }

    private static string ReadFrom(string? from) =>
        MailAddress.TryCreate(from, out var sender)
            ? sender.Address
            : throw new InvalidOperationException(
                $"{FromKey} is not an email address, and a message needs somebody to be "
                + "from.");

    /// <remarks>
    /// An origin and nothing else: no path, no query, no fragment, no trailing
    /// slash. Links are assembled from it by appending, so anything else would
    /// produce an address that is subtly wrong in every message rather than
    /// obviously wrong once.
    /// </remarks>
    private static Uri? ReadPublicUrl(string? publicUrl, bool development)
    {
        if (string.IsNullOrWhiteSpace(publicUrl))
        {
            return null;
        }

        var trimmed = publicUrl.Trim();

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var url)
            || url.Scheme is not ("http" or "https")
            || url.AbsolutePath != "/"
            || url.Query.Length > 0
            || url.Fragment.Length > 0
            || trimmed.EndsWith('/'))
        {
            throw new InvalidOperationException(
                $"{PublicUrlKey} is '{publicUrl}', which is not an absolute http or https "
                + "origin without a trailing slash — `https://logs.example.com`.");
        }

        return !development && url.Scheme != "https"
            ? throw new InvalidOperationException(
                $"{PublicUrlKey} is '{publicUrl}'. An installation on the open internet is "
                + "behind TLS, and a link sent by mail that is not is a link somebody "
                + "clicks in the clear.")
            : url;
    }
}
