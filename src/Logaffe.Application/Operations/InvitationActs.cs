using Logaffe.Application.Ports;
using Logaffe.Domain.Identities;

namespace Logaffe.Application.Operations;

/// <summary>
/// How an invitation ended.
/// </summary>
public enum InviteOutcome
{
    /// <summary>The account exists, invited, and the message went out.</summary>
    Invited,

    /// <summary>
    /// Somebody already holds that address. Said plainly, because this is an
    /// administrator inviting a colleague and the useful answer is that the
    /// person is already here.
    /// </summary>
    AddressTaken,

    /// <summary>Not an address, or not a name.</summary>
    NotAnAddress,

    /// <summary>
    /// This installation has no SMTP configured, so there is no way to send
    /// somebody a link. Nothing was written (ADR 0053).
    /// </summary>
    NoMail,

    /// <summary>
    /// The account was created and the message did not go out. It is its own
    /// outcome because the state afterwards is real: the person exists, invited,
    /// and re-inviting them is what sends a fresh link.
    /// </summary>
    NotDelivered,
}

/// <summary>
/// An administrator asking somebody to join (ADR 0053).
/// </summary>
/// <remarks>
/// <para>
/// It writes the account in <see cref="UserState.Invited"/> — an address and no
/// password — and sends a link that sets the first one. <b>They begin with no
/// project access at all</b>: an invitation grants an account, not a view
/// (ADR 0055).
/// </para>
/// <para>
/// <b>The account is written before the message goes out.</b> The other order
/// would mean sending somebody a link to an account that does not exist if the
/// write then failed. This way a failed delivery leaves an invited account and a
/// spent secret, which re-inviting replaces — and which an administrator can see
/// in the list rather than having to guess at.
/// </para>
/// </remarks>
public sealed class InviteAUser(
    IIdentities identities,
    IOneTimeSecrets secrets,
    IMail mail,
    MailTemplates templates,
    TimeProvider clock)
{
    /// <param name="administrator">Whether the invited account administers.</param>
    public async Task<InviteOutcome> ExecuteAsync(
        string? name,
        string? email,
        bool administrator,
        CancellationToken cancellationToken)
    {
        // Asked first, because an installation that cannot send has nothing to
        // offer here and writing the account would leave one nobody can reach.
        if (!mail.IsConfigured)
        {
            return InviteOutcome.NoMail;
        }

        User invited;
        try
        {
            invited = User.Invite(name!, email!, administrator, clock.GetUtcNow());
        }
        catch (ArgumentException)
        {
            return InviteOutcome.NotAnAddress;
        }

        if (!await identities.TryAddAsync(invited, cancellationToken))
        {
            return InviteOutcome.AddressTaken;
        }

        return await new SendALink(secrets, mail, templates, clock).ExecuteAsync(
            invited, OneTimeSecretPurpose.Invitation, cancellationToken)
            ? InviteOutcome.Invited
            : InviteOutcome.NotDelivered;
    }
}

/// <summary>
/// Sending the same invitation again, to somebody who has not arrived yet.
/// </summary>
/// <remarks>
/// It issues a fresh secret rather than resending the old one, which is what
/// makes a lost message and an expired link the same event with the same answer.
/// The previous link stops working in the same transaction.
/// </remarks>
public sealed class ReinviteAUser(
    IIdentities identities,
    IOneTimeSecrets secrets,
    IMail mail,
    MailTemplates templates,
    TimeProvider clock)
{
    public async Task<InviteOutcome> ExecuteAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (!mail.IsConfigured)
        {
            return InviteOutcome.NoMail;
        }

        var invited = await identities.FindUserAsync(userId, cancellationToken);

        // Somebody who already has a password is not waiting for an invitation,
        // and sending them one would be a link that sets a password without
        // asking for the current one.
        if (invited is null || invited.State is not UserState.Invited)
        {
            return InviteOutcome.NotAnAddress;
        }

        return await new SendALink(secrets, mail, templates, clock).ExecuteAsync(
            invited, OneTimeSecretPurpose.Invitation, cancellationToken)
            ? InviteOutcome.Invited
            : InviteOutcome.NotDelivered;
    }
}

/// <summary>
/// Drawing a secret, writing it, and sending the link it goes into — the three
/// steps every one of the three purposes takes, in the one order that is safe.
/// </summary>
/// <remarks>
/// <para>
/// Written before sent. A message carrying a link the installation has not
/// stored is a link that opens nothing, which is worse than a stored secret
/// nobody received: the second is a re-issue away from working.
/// </para>
/// <para>
/// <b>A delivery failure is a <c>false</c> and not an exception here.</b> Every
/// caller has something to say about it, and none of them can undo the write —
/// there is no queue, and retrying issues a fresh secret (ADR 0053).
/// </para>
/// </remarks>
public sealed class SendALink(
    IOneTimeSecrets secrets, IMail mail, MailTemplates templates, TimeProvider clock)
{
    public async Task<bool> ExecuteAsync(
        User user,
        OneTimeSecretPurpose purpose,
        CancellationToken cancellationToken,
        string? pendingEmail = null)
    {
        var now = clock.GetUtcNow();
        var issued = OneTimeSecret.Issue(user.Id, purpose, now, pendingEmail);

        await secrets.IssueAsync(issued.Secret, now, cancellationToken);

        try
        {
            await mail.SendAsync(
                templates.For(user, purpose, issued.Value, pendingEmail), cancellationToken);

            return true;
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            // The adapter logged what the server said (ADR 0053). What is left
            // to do here is tell the caller, who tells the person who pressed
            // the button.
            return false;
        }
    }
}
