namespace Logaffe.Domain.Alerts;

/// <summary>
/// Where this installation's notifications go: a server, a topic, and an
/// optional access token.
/// </summary>
/// <remarks>
/// <para>
/// There is one of these for the installation and there is no second kind of
/// one (<c>docs/alerts.md</c>). A notification that is a name, three numbers and
/// a URL formats identically everywhere, so the argument for a second
/// integration is not that the first renders poorly — and email in particular
/// stays absent because this product has no address to send anything to
/// (ADR 0015).
/// </para>
/// <para>
/// <b>The token is held sealed and nothing here can open it.</b> What this
/// carries is what the row carries: the bytes <c>ISecretCipher</c> made, under
/// the key on the host volume (ADR 0022). It is an access token rather than a
/// password because a public ntfy topic needs none at all, which is the
/// arrangement most self-hosters will be on.
/// </para>
/// <para>
/// <b>Both strings are validated rather than stored as typed.</b> A topic is a
/// path segment on somebody else's server and the server is an address this
/// installation will post to unattended, so neither is somewhere arbitrary text
/// is kept: a value that is not one is refused at the settings screen instead of
/// discovered on the night an alert was needed.
/// </para>
/// </remarks>
public sealed record Notifier
{
    /// <summary>
    /// What ntfy accepts as a topic, which is what a wrong one is measured
    /// against.
    /// </summary>
    public const int TopicMaxLength = 64;

    private Notifier(Uri server, string topic, byte[]? encryptedAccessToken)
    {
        Server = server;
        Topic = topic;
        EncryptedAccessToken = encryptedAccessToken;
    }

    /// <summary>
    /// The ntfy server, as an absolute <c>http</c> or <c>https</c> address.
    /// </summary>
    /// <remarks>
    /// A path is allowed and kept, because an ntfy behind a proxy is commonly
    /// under one; a query and a fragment are not, because a topic is appended to
    /// this and neither survives that in any useful form.
    /// </remarks>
    public Uri Server { get; }

    /// <summary>The topic on it, which is the whole of the addressing.</summary>
    public string Topic { get; }

    /// <summary>
    /// The access token as the row holds it, or <c>null</c> for the public topic
    /// that needs none.
    /// </summary>
    public byte[]? EncryptedAccessToken { get; }

    /// <inheritdoc cref="TryCreate"/>
    /// <exception cref="ArgumentException">
    /// The server is not an absolute <c>http</c> or <c>https</c> address, or the
    /// topic is not a topic.
    /// </exception>
    public static Notifier Create(string? server, string? topic, byte[]? encryptedAccessToken) =>
        TryCreate(server, topic, encryptedAccessToken, out var notifier)
            ? notifier
            : throw new ArgumentException(
                "A notifier is an absolute http or https server and a topic of at most "
                + $"{TopicMaxLength} letters, digits, hyphens and underscores.",
                nameof(server));

    /// <summary>
    /// Whether the two strings name a notifier, and the notifier they name.
    /// </summary>
    public static bool TryCreate(
        string? server, string? topic, byte[]? encryptedAccessToken, out Notifier notifier)
    {
        notifier = null!;

        var trimmed = topic?.Trim();
        if (!IsTopic(trimmed) || !TryServer(server, out var address))
        {
            return false;
        }

        notifier = new Notifier(address, trimmed!, encryptedAccessToken);
        return true;
    }

    /// <summary>
    /// Whether <paramref name="value"/> is an address a notifier can sit at.
    /// </summary>
    /// <remarks>
    /// The two halves are asked separately as well as together, because a screen
    /// taking them from a person names the box that is wrong
    /// (<c>docs/setup.md</c>) and "one of these two is not right" is not that.
    /// </remarks>
    public static bool IsServer(string? value) => TryServer(value, out _);

    /// <inheritdoc cref="IsServer"/>
    public static bool IsTopic(string? value)
    {
        var trimmed = value?.Trim();

        return trimmed is { Length: > 0 and <= TopicMaxLength } && IsWrittenAsATopic(trimmed);
    }

    /// <summary>
    /// The same notifier with the token it is stored with, which is how the
    /// operator changing the server keeps the token they already sealed.
    /// </summary>
    public Notifier Sealing(byte[]? encryptedAccessToken) =>
        new(Server, Topic, encryptedAccessToken);

    /// <summary>Where a notification is posted, which is the topic on the server.</summary>
    public Uri Endpoint => new(Server, Topic);

    private static bool TryServer(string? value, out Uri server)
    {
        server = null!;

        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed)
            || !Uri.TryCreate(trimmed, UriKind.Absolute, out var parsed)
            || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps)
            || parsed.Query.Length > 0
            || parsed.Fragment.Length > 0
            || parsed.UserInfo.Length > 0)
        {
            return false;
        }

        // A trailing slash, so that appending the topic keeps the path the
        // operator wrote rather than replacing its last segment: `Uri` resolves
        // `topic` against `https://example.com/ntfy` as `https://example.com/topic`.
        server = parsed.AbsolutePath.EndsWith('/')
            ? parsed
            : new Uri(parsed + "/");

        return true;
    }

    private static bool IsWrittenAsATopic(string value)
    {
        foreach (var character in value)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character != '-' && character != '_')
            {
                return false;
            }
        }

        return true;
    }
}
