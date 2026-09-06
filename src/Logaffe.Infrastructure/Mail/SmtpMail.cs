using Logaffe.Application.Ports;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace Logaffe.Infrastructure.Mail;

/// <summary>
/// One message, sent as far as the server accepting it (ADR 0053).
/// </summary>
/// <remarks>
/// <para>
/// <b>There is no queue behind this and there will not be one.</b> The
/// connection is opened, the message is handed over, the connection is closed,
/// and what the caller gets back is whether that worked. A durable queue would
/// be a second store, a background worker and a class of failure that happens
/// where nobody is looking — for three kinds of message that a human is waiting
/// on anyway, retrying by hand is the better trade.
/// </para>
/// <para>
/// <b>A failure is logged and then rethrown.</b> The log is where an operator
/// reads what the server actually said (ADR 0002); the exception is what makes
/// the screen say the invitation did not go out, which is the half that matters
/// to the person who pressed the button.
/// </para>
/// </remarks>
public sealed class SmtpMail(SmtpSettings settings, ILogger<SmtpMail> logger) : IMail
{
    public bool IsConfigured => settings.Configured;

    public async Task SendAsync(Application.Ports.Mail mail, CancellationToken cancellationToken)
    {
        if (!settings.Configured)
        {
            throw new InvalidOperationException(
                "This installation has no SMTP configured, so it cannot send.");
        }

        var message = new MimeMessage
        {
            Subject = mail.Subject,
            Body = new BodyBuilder { TextBody = mail.Text, HtmlBody = mail.Html }.ToMessageBody(),
        };

        message.From.Add(new MailboxAddress(settings.FromName, settings.From!));
        message.To.Add(MailboxAddress.Parse(mail.To));

        try
        {
            using var client = new SmtpClient();

            await client.ConnectAsync(
                settings.Host!, settings.Port, Socket(settings.Security), cancellationToken);

            // Only when there are credentials. A server that takes mail from
            // its own network — which is what Mailpit is and what a relay on the
            // same host may be — is asked for nothing.
            if (settings.Username is not null)
            {
                await client.AuthenticateAsync(
                    settings.Username, settings.Password!, cancellationToken);
            }

            await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(quit: true, cancellationToken);
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            logger.LogError(
                failure,
                "Sending {Subject} through {Host}:{Port} failed. Nothing was queued: the act "
                + "that asked for it says so, and retrying issues a fresh secret.",
                mail.Subject,
                settings.Host,
                settings.Port);

            throw;
        }
    }

    private static SecureSocketOptions Socket(SmtpSecurity security) => security switch
    {
        SmtpSecurity.StartTls => SecureSocketOptions.StartTls,
        SmtpSecurity.Tls => SecureSocketOptions.SslOnConnect,
        SmtpSecurity.None => SecureSocketOptions.None,
        _ => throw new ArgumentOutOfRangeException(
            nameof(security), security, "Unknown transport security."),
    };
}
