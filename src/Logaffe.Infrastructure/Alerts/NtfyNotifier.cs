using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Logaffe.Application.Ports;
using Logaffe.Domain.Alerts;
using Microsoft.Extensions.Logging;

namespace Logaffe.Infrastructure.Alerts;

/// <summary>
/// The one notifier: a name, its numbers and a URL, posted to a topic on an
/// ntfy server.
/// </summary>
/// <remarks>
/// <para>
/// ntfy is the one this product supports and there is no second — it pushes, it
/// needs no inbound port, it is self-hostable and it reaches a phone, and a
/// notification this shape formats identically everywhere, so the case for a
/// second integration was never that the first renders poorly
/// (<c>docs/alerts.md</c>).
/// </para>
/// <para>
/// <b>It sends once and it is finished.</b> A failure costs one line in the
/// installation's own file log (ADR 0002) and nothing else: no retry, no queue,
/// and no second attempt on the next pass. A queue of undelivered alerts
/// arriving together an hour later is a burst of notifications about things that
/// are no longer true, which is the fastest way to teach an operator to swipe
/// them away — the alert that was missed is the smaller cost.
/// </para>
/// <para>
/// <b>An installation with no notifier is a real state, not a placeholder.</b>
/// An operator switches a condition on before they configure a notifier, or
/// clears the notifier afterwards, and the line in the file log is what makes
/// that legible instead of silent.
/// </para>
/// <para>
/// <b>It publishes as JSON rather than as ntfy's headers.</b> A project is
/// called what the operator called it, an umlaut is ordinary in this
/// installation's own language, and a header is ASCII: the JSON form carries a
/// name as it was written instead of as an encoded word some clients render and
/// others do not.
/// </para>
/// </remarks>
public sealed class NtfyNotifier(
    HttpClient http,
    IInstallation installation,
    ISecretCipher cipher,
    AlertLinks links,
    ILogger<NtfyNotifier> logger) : IAlertNotifier
{
    public async Task SendAsync(Alert alert, CancellationToken cancellationToken)
    {
        var notifier = await installation.ReadNotifierAsync(cancellationToken);
        if (notifier is null)
        {
            logger.LogWarning(
                "The {Condition} condition fired for {Subject} and this installation "
                + "has no notifier configured: {Alert}",
                alert.Condition,
                alert.SubjectName,
                alert);

            return;
        }

        var proof = await PostAsync(
            notifier, NtfyMessage.For(alert, links.For(alert)), cancellationToken);

        if (proof is not NotifierProof.Sent)
        {
            // One line, and the alert is gone. It says which condition and which
            // subject, because an operator reading this later is asking what
            // they were not told about.
            logger.LogWarning(
                "The {Condition} condition fired for {Subject} and the notifier at "
                + "{Endpoint} did not take it ({Proof}). There is no retry: {Alert}",
                alert.Condition,
                alert.SubjectName,
                notifier.Endpoint,
                proof,
                alert);
        }
    }

    public async Task<NotifierProof> SendTestAsync(CancellationToken cancellationToken)
    {
        var notifier = await installation.ReadNotifierAsync(cancellationToken);

        return notifier is null
            ? NotifierProof.NoNotifier
            : await PostAsync(notifier, NtfyMessage.Test(links.Home), cancellationToken);
    }

    /// <remarks>
    /// <para>
    /// Nothing thrown here reaches the caller. The pass that decides has other
    /// projects to evaluate and a throw would take them with it, and the
    /// operator pressing the test button wants an answer rather than a stack
    /// trace — so every way this can fail comes back as one of the four words.
    /// </para>
    /// <para>
    /// <b>The two refusals are told apart on purpose.</b> A server that says no
    /// is a token that is wrong or a topic this one may not publish to, and the
    /// operator's next move is the notifier's own settings; anything else is the
    /// address, the network or the server itself, and the next move is
    /// elsewhere.
    /// </para>
    /// </remarks>
    private async Task<NotifierProof> PostAsync(
        Notifier notifier, NtfyMessage message, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, notifier.Server)
            {
                Content = JsonContent.Create(
                    new Publication(
                        notifier.Topic,
                        message.Title,
                        message.Body,
                        message.Link?.ToString())),
            };

            if (notifier.EncryptedAccessToken is { } sealedToken)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue(
                    "Bearer", cipher.Decrypt(sealedToken));
            }

            using var answer = await http.SendAsync(request, cancellationToken);

            return answer.IsSuccessStatusCode
                ? NotifierProof.Sent
                : answer.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                    ? NotifierProof.Refused
                    : NotifierProof.Unreachable;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The installation is stopping, which is not the notifier's fault
            // and not a thing to write a line about.
            throw;
        }
        catch (Exception thrown) when (thrown is HttpRequestException or OperationCanceledException)
        {
            // A name that does not resolve, a certificate that is not accepted,
            // a server that is not there, or one that took longer than the
            // handful of seconds an hourly pass can wait.
            return NotifierProof.Unreachable;
        }
    }

    /// <summary>
    /// What ntfy takes: the topic in the body, so that one address serves every
    /// topic and nothing has to be escaped into a path.
    /// </summary>
    private sealed record Publication(
        [property: JsonPropertyName("topic")] string Topic,
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("message")] string Message,
        [property: JsonPropertyName("click")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? Click);
}
