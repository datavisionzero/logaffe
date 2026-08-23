using Logaffe.Application.Ports;
using Logaffe.Domain.Alerts;

namespace Logaffe.Application.Operations;

/// <summary>
/// The notifier as the operator sees it, with the token in the clear.
/// </summary>
/// <remarks>
/// This is the read-back ADR 0022 exists for, and it is the same arrangement a
/// token has: what the row holds is sealed under the key on the host volume, and
/// there is one act that opens it rather than a list that does so on the way
/// past. A screen showing which server this installation notifies through has
/// not read a secret; it has read a server and a topic.
/// </remarks>
public sealed record TheNotifier(string Server, string Topic, string? AccessToken);

/// <summary>
/// The operator naming the one place this installation's notifications go, or
/// taking it away again.
/// </summary>
/// <remarks>
/// <para>
/// One notifier for the installation, and it is ntfy (<c>docs/alerts.md</c>).
/// There is no second destination, no per-condition one, and nothing here that
/// chooses a provider — so what this act takes is a server, a topic and an
/// optional token, and there is no fourth argument for the kind.
/// </para>
/// <para>
/// <b>The token is sealed here rather than in the store</b>, which is where
/// every other secret in this product is sealed: the act holds the cipher and
/// the store holds bytes (ADR 0022). Keeping the token means saying so — an
/// operator correcting a topic is not re-typing a secret they cannot see — so
/// the token that is not supplied is the token that was already there, and
/// clearing it is clearing the notifier.
/// </para>
/// </remarks>
public sealed class ChangeTheNotifier(IInstallation installation, ISecretCipher cipher)
{
    /// <summary>
    /// Writes the notifier down.
    /// </summary>
    /// <param name="accessToken">
    /// The token to seal, <c>null</c> to keep whatever is already sealed, or the
    /// empty string to have no token at all — which is the public topic most
    /// self-hosters will be on. The three are distinct because a screen cannot
    /// show a secret it is about to overwrite: an operator correcting a topic
    /// sends no token, and one moving to an unauthenticated server has to say
    /// so.
    /// </param>
    /// <exception cref="ArgumentException">
    /// The server is not an absolute <c>http</c> or <c>https</c> address, or the
    /// topic is not a topic. A screen taking these from a person says so before
    /// it gets here; the domain refusing them is the backstop.
    /// </exception>
    public async Task ExecuteAsync(
        string? server, string? topic, string? accessToken, CancellationToken cancellationToken)
    {
        var notifier = Notifier.Create(server, topic, encryptedAccessToken: null);

        var sealedToken = accessToken is null
            ? (await installation.ReadNotifierAsync(cancellationToken))?.EncryptedAccessToken
            : accessToken.Length == 0
                ? null
                : cipher.Encrypt(accessToken);

        await installation.RecordNotifierAsync(
            notifier.Sealing(sealedToken), cancellationToken);
    }

    /// <summary>
    /// Takes the notifier away, and the token with it.
    /// </summary>
    /// <remarks>
    /// The conditions are not switched off by this and nothing here says they
    /// should be: an operator who clears a notifier while a condition is on has
    /// an alert that costs one line in the installation's own log, which is a
    /// real state and legible where the switch is.
    /// </remarks>
    public Task ClearAsync(CancellationToken cancellationToken) =>
        installation.RecordNotifierAsync(null, cancellationToken);
}

/// <summary>
/// The notifier this installation holds, put back together for the operator.
/// </summary>
/// <inheritdoc cref="TheNotifier" path="/remarks"/>
public sealed class ReadTheNotifier(IInstallation installation, ISecretCipher cipher)
{
    /// <summary>
    /// The notifier as it was configured, or <c>null</c> on an installation that
    /// has none.
    /// </summary>
    /// <remarks>
    /// A sealed token this cipher cannot open throws rather than answering a
    /// notifier without one: it is a corrupt row or a lost key — an
    /// installation-level fault the startup check exists to catch — and
    /// answering "no token" would tell the operator their public topic stopped
    /// working for some other reason.
    /// </remarks>
    public async Task<TheNotifier?> ExecuteAsync(CancellationToken cancellationToken)
    {
        var notifier = await installation.ReadNotifierAsync(cancellationToken);

        return notifier is null
            ? null
            : new TheNotifier(
                notifier.Server.ToString(),
                notifier.Topic,
                notifier.EncryptedAccessToken is { } sealedToken
                    ? cipher.Decrypt(sealedToken)
                    : null);
    }
}

/// <summary>
/// The operator proving the notifier works, before the night it is needed.
/// </summary>
/// <remarks>
/// <para>
/// It is theirs rather than any condition's, and it sends the shape a real alert
/// has: a name, its numbers and a link, and nothing that came out of an entry
/// (ADR 0049). Nothing about it is stored — there is no last-tested-at, because
/// what a notifier did five minutes ago is not evidence about what it will do
/// tonight, and the answer is on the screen of the person who pressed it.
/// </para>
/// <para>
/// <b>It is the one send in this product that answers.</b> Everything else about
/// alerting fails silently by design — a failed send is one line in a log file,
/// with no retry and no queue — which is exactly why an installation nobody has
/// tested is one nobody knows about.
/// </para>
/// </remarks>
public sealed class SendATestNotification(IAlertNotifier notifier)
{
    public Task<NotifierProof> ExecuteAsync(CancellationToken cancellationToken) =>
        notifier.SendTestAsync(cancellationToken);
}
