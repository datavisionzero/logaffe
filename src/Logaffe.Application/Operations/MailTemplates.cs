using System.Globalization;
using Logaffe.Application.Ports;
using Logaffe.Domain.Identities;

namespace Logaffe.Application.Operations;

/// <summary>
/// What the three messages say, in both forms (ADR 0053).
/// </summary>
/// <remarks>
/// <para>
/// They live in the application layer because they are product text rather than
/// a fact about SMTP — the adapter takes a subject and two bodies and knows
/// nothing about what is in them.
/// </para>
/// <para>
/// <b>Every link is built from the public base URL and never from a request.</b>
/// Two of these are composed with nobody on the other end, and the third is
/// composed for somebody who is not the person the link is for; a link assembled
/// from an inbound header is a link an attacker chooses.
/// </para>
/// <para>
/// <b>Written out rather than templated.</b> Three messages of a paragraph each
/// do not earn a template engine, a layout, or a partial — what they earn is
/// being readable in the file that sends them. They carry no logo, no tracking
/// pixel and no unsubscribe footer: there is nothing to unsubscribe from, and
/// nothing here is a mailing.
/// </para>
/// </remarks>
public sealed class MailTemplates(SmtpSettings settings)
{
    public Mail For(
        User user, OneTimeSecretPurpose purpose, string secret, string? pendingEmail) =>
        purpose switch
        {
            OneTimeSecretPurpose.Invitation => Invitation(user, secret),
            OneTimeSecretPurpose.PasswordRecovery => Recovery(user, secret),
            OneTimeSecretPurpose.EmailChange => Change(user, secret, pendingEmail!),
            _ => throw new ArgumentOutOfRangeException(
                nameof(purpose), purpose, "Unknown purpose."),
        };

    private Mail Invitation(User user, string secret)
    {
        var link = Link("invitation", secret);
        var days = OneTimeSecret.InvitationLifetime.TotalDays.ToString(
            CultureInfo.InvariantCulture);

        return new Mail(
            user.Email,
            $"You have been invited to {Installation}",
            $"""
             Somebody invited you to {Installation}, a logaffe installation.

             Set your password to finish:

             {link}

             The link works once and expires in {days} days. If it has, ask
             whoever invited you to send another.

             If you were not expecting this, ignore it — nothing happens until
             somebody sets a password.
             """,
            Html(
                $"Somebody invited you to <strong>{Installation}</strong>, a logaffe "
                + "installation.",
                "Set your password",
                link,
                $"The link works once and expires in {days} days. If you were not expecting "
                + "this, ignore it — nothing happens until somebody sets a password."));
    }

    private Mail Recovery(User user, string secret)
    {
        var link = Link("recovery", secret);
        var hours = OneTimeSecret.Lifetime.TotalHours.ToString(CultureInfo.InvariantCulture);

        return new Mail(
            user.Email,
            $"Setting a new password on {Installation}",
            $"""
             Somebody asked for a new password on {Installation}, a logaffe
             installation.

             Set one:

             {link}

             The link works once and expires in {hours} hour. It ends every
             session this account has, everywhere.

             If it was not you, ignore this. Your password has not changed, and
             nobody can set one without this link.
             """,
            Html(
                $"Somebody asked for a new password on <strong>{Installation}</strong>, a "
                + "logaffe installation.",
                "Set a new password",
                link,
                $"The link works once and expires in {hours} hour, and it ends every session "
                + "this account has. If it was not you, ignore this: your password has not "
                + "changed."));
    }

    private Mail Change(User user, string secret, string pendingEmail)
    {
        var link = Link("address", secret);
        var hours = OneTimeSecret.Lifetime.TotalHours.ToString(CultureInfo.InvariantCulture);

        // Sent to the address being moved to and not to the one signed in with:
        // what this proves is that somebody can read mail at the new address,
        // and sending it to the old one would prove nothing.
        return new Mail(
            pendingEmail,
            $"Confirming this address on {Installation}",
            $"""
             Somebody is moving their {Installation} account to this address.

             Confirm it:

             {link}

             The link works once and expires in {hours} hour. Until it is used,
             {user.Email} is still the address that signs in.

             If this is not yours, ignore it. Nothing about the account changes.
             """,
            Html(
                $"Somebody is moving their <strong>{Installation}</strong> account to this "
                + "address.",
                "Confirm this address",
                link,
                $"The link works once and expires in {hours} hour. Until it is used, the old "
                + "address is still the one that signs in. If this is not yours, ignore it."));
    }

    /// <summary>
    /// What the installation is called in a message. It is the host of the
    /// public base URL rather than a name somebody set, because that is the
    /// thing the person reading recognizes and it cannot drift from where the
    /// link goes.
    /// </summary>
    private string Installation => settings.PublicUrl?.Host ?? "logaffe";

    private string Link(string path, string secret) =>
        $"{settings.PublicUrl}{path}?secret={Uri.EscapeDataString(secret)}";

    /// <summary>
    /// The HTML half: one paragraph, one button, one line of small print, and
    /// the address written out underneath it for the client that will not render
    /// a link.
    /// </summary>
    private static string Html(string opening, string action, string link, string closing) =>
        $"""
         <div style="font-family:system-ui,sans-serif;font-size:15px;line-height:1.5">
           <p>{opening}</p>
           <p><a href="{link}"
                 style="display:inline-block;padding:10px 16px;border-radius:6px;
                        background:#111;color:#fff;text-decoration:none">{action}</a></p>
           <p style="color:#555;font-size:13px">{closing}</p>
           <p style="color:#555;font-size:13px">{link}</p>
         </div>
         """;
}
