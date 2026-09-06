namespace Logaffe.Application.Ports;

/// <summary>
/// One transactional message, in both forms a mail client may render.
/// </summary>
/// <remarks>
/// Text and HTML are written separately rather than one being generated from
/// the other. There are three of these in the whole product and each is a
/// paragraph and a link; a converter would be a dependency and a class of
/// rendering bug for messages that fit on a postcard.
/// </remarks>
public sealed record Mail(string To, string Subject, string Text, string Html);

/// <summary>
/// The one way anything in this product reaches an inbox (ADR 0053).
/// </summary>
/// <remarks>
/// <para>
/// <b>It is for identity transactions and nothing else</b>: an invitation, a
/// password recovery, the confirmation of a changed address. An alert does not
/// come through here — that is ntfy's, and the reason is push to a phone rather
/// than an address the product now happens to have
/// ([ADR 0049](../../../docs/adr/0049-a-notification-carries-numbers-and-names-never-log-content.md)).
/// </para>
/// <para>
/// <b>A delivery failure is thrown and not swallowed.</b> There is no queue, no
/// retry engine and no background worker: the person who pressed invite is told
/// that the message did not go out, and retrying issues a fresh secret rather
/// than resending the old one. That is the whole of the trade ADR 0053 makes.
/// </para>
/// </remarks>
public interface IMail
{
    /// <summary>
    /// Whether this installation can send at all. An installation with no SMTP
    /// configured is healthy and complete in every other respect, so the acts
    /// that need mail ask this first and refuse with a sentence rather than
    /// failing obscurely.
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Sends one message, and returns when the server has accepted it.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// This installation has no SMTP configured. Callers ask
    /// <see cref="IsConfigured"/> first; this is the backstop.
    /// </exception>
    Task SendAsync(Mail mail, CancellationToken cancellationToken);
}
