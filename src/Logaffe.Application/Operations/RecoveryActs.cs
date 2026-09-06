using Logaffe.Application.Ports;
using Logaffe.Domain.Identities;

namespace Logaffe.Application.Operations;

/// <summary>
/// How redeeming a link ended.
/// </summary>
/// <remarks>
/// A link that was never one, one that was spent, one that expired and one
/// belonging to an account that has since been deactivated are
/// <see cref="LinkRefused"/> — a single answer, because telling them apart would
/// say what the installation holds to somebody holding a value it does not
/// recognize.
/// </remarks>
public enum RedeemOutcome
{
    Redeemed,

    /// <summary>The link opens nothing, whatever the reason.</summary>
    LinkRefused,

    /// <summary>Shorter than a password may be, or longer than one is hashed.</summary>
    PasswordNotOne,

    /// <summary>
    /// Somebody took the address while the change was outstanding. Only a change
    /// of address can end this way.
    /// </summary>
    AddressTaken,
}

/// <summary>
/// Somebody asking for their password back (ADR 0053).
/// </summary>
/// <remarks>
/// <para>
/// <b>It never says whether an address exists.</b> The answer is the same
/// sentence for an address nobody holds, one that belongs to somebody who was
/// invited and never arrived, and one whose account has been deactivated: a
/// message has been sent if there is an account. Saying otherwise would turn a
/// public form into a way of asking who is here.
/// </para>
/// <para>
/// It is public and pre-authentication, so it carries the ordinary rate limit.
/// It is not counted against the sign-in windows: those are about guessing a
/// password, and nothing is guessed here.
/// </para>
/// </remarks>
public sealed class BeginRecovery(
    IIdentities identities,
    IOneTimeSecrets secrets,
    IMail mail,
    MailTemplates templates,
    TimeProvider clock)
{
    /// <returns>
    /// Nothing. There is no outcome to report that would not be an answer about
    /// the address, and the screen says the same thing either way.
    /// </returns>
    public async Task ExecuteAsync(string? email, CancellationToken cancellationToken)
    {
        if (!mail.IsConfigured)
        {
            return;
        }

        string normalized;
        try
        {
            normalized = User.NormalizeEmailForComparison(email);
        }
        catch (ArgumentException)
        {
            return;
        }

        var user = await identities.FindByEmailAsync(normalized, cancellationToken);

        // An account that was invited and never arrived is left to its
        // invitation: a recovery link would be a second way to set a first
        // password, and one of them would be the one nobody expected.
        if (user is null || !user.IsActive)
        {
            return;
        }

        await new SendALink(secrets, mail, templates, clock).ExecuteAsync(
            user, OneTimeSecretPurpose.PasswordRecovery, cancellationToken);
    }
}

/// <summary>
/// The other half of the three links: setting a first password, setting a new
/// one, or confirming an address (ADR 0053).
/// </summary>
/// <remarks>
/// <para>
/// One act for three purposes, because what is dangerous about them is the same:
/// finding the secret, refusing one that no longer opens anything, spending it
/// before acting, and ending sessions afterwards where the credential changed.
/// </para>
/// <para>
/// <b>The secret is spent before the account is touched.</b> Two requests
/// carrying one link must not both win, and the second one finds it spent —
/// which is the row's own doing rather than a check this ran first.
/// </para>
/// </remarks>
public sealed class RedeemALink(
    IIdentities identities,
    IOneTimeSecrets secrets,
    ISessions sessions,
    IPasswordHasher hasher,
    TimeProvider clock)
{
    /// <param name="password">
    /// The password being set, for an invitation and a recovery; ignored by a
    /// change of address, which changes no credential.
    /// </param>
    public async Task<RedeemOutcome> ExecuteAsync(
        OneTimeSecretPurpose purpose,
        string? secret,
        string? password,
        CancellationToken cancellationToken)
    {
        var hash = OneTimeSecret.Hash(secret);
        if (hash is null)
        {
            return RedeemOutcome.LinkRefused;
        }

        // The shape of the password before the table is read, and before the
        // hasher: this surface is public, and hashing is deliberately slow.
        Password? chosen = null;
        if (purpose is not OneTimeSecretPurpose.EmailChange
            && !Password.TryCreate(password, out chosen))
        {
            return RedeemOutcome.PasswordNotOne;
        }

        var held = await secrets.FindAsync(hash, cancellationToken);
        var now = clock.GetUtcNow();

        // One answer for a link that was never one, one that was spent, one that
        // expired, and one for the wrong act.
        if (held is null || held.Purpose != purpose || !held.IsLive(now))
        {
            return RedeemOutcome.LinkRefused;
        }

        var user = await identities.FindUserAsync(held.UserId, cancellationToken);
        if (user is null || user.State is UserState.Deactivated)
        {
            return RedeemOutcome.LinkRefused;
        }

        // The address is checked at the moment of redeeming and not at the
        // moment of asking: somebody may have taken it in between, and the
        // unique index would otherwise surface as a failed request rather than
        // as a sentence.
        if (purpose is OneTimeSecretPurpose.EmailChange
            && await identities.FindByEmailAsync(
                held.PendingNormalizedEmail!, cancellationToken) is not null)
        {
            return RedeemOutcome.AddressTaken;
        }

        held.ConsumeAt(now);
        await secrets.RecordAsync(held, cancellationToken);

        switch (purpose)
        {
            case OneTimeSecretPurpose.Invitation:
                user.ActivateWith(hasher.Hash(chosen!));
                break;

            case OneTimeSecretPurpose.PasswordRecovery:
                user.ChangePasswordTo(hasher.Hash(chosen!));
                break;

            case OneTimeSecretPurpose.EmailChange:
                user.ChangeEmailTo(held.PendingEmail!);
                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(purpose), purpose, "Unknown purpose.");
        }

        await identities.RecordAsync(user, cancellationToken);

        // Every session, and not every other: whoever is redeeming a recovery
        // link is not signed in, and a session that survived it would be the one
        // this act exists to end. A change of address ends none — the credential
        // did not change.
        if (purpose is OneTimeSecretPurpose.PasswordRecovery)
        {
            await sessions.RemoveEveryOfAsync(user.Id, cancellationToken);
        }

        return RedeemOutcome.Redeemed;
    }
}

/// <summary>
/// How a change of address ended.
/// </summary>
public enum ChangeAddressOutcome
{
    /// <summary>A link went to the new address, and nothing has changed yet.</summary>
    Sent,

    /// <summary>Somebody holds it, or a live change is already moving to it.</summary>
    AddressTaken,

    /// <summary>Not an address.</summary>
    NotAnAddress,

    /// <summary>That is not their password.</summary>
    PasswordRefused,

    /// <summary>No SMTP, so there is no way to prove the new address.</summary>
    NoMail,

    /// <summary>The link did not go out. Nothing about the account changed.</summary>
    NotDelivered,
}

/// <summary>
/// Somebody moving their account to another address (ADR 0053).
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing changes until the link is redeemed.</b> Until then the old address
/// is still the one that signs in, so a change that is asked for and never
/// confirmed costs nothing at all — and the link goes to the new address,
/// because what it proves is that somebody can read mail there.
/// </para>
/// <para>
/// <b>The new address is reserved while the change is live.</b> Against the
/// people who hold one, and against other outstanding changes: two people moving
/// to one address would both be told yes and one of them would meet the unique
/// index at the end of it.
/// </para>
/// <para>
/// It asks for the password, like everything else on somebody's own credentials:
/// an unlocked browser must not be able to move an account somewhere its owner
/// cannot read.
/// </para>
/// </remarks>
public sealed class ChangeAddress(
    IIdentities identities,
    IOneTimeSecrets secrets,
    IMail mail,
    MailTemplates templates,
    IPasswordHasher hasher,
    TimeProvider clock)
{
    public async Task<ChangeAddressOutcome> ExecuteAsync(
        User user, string? password, string? email, CancellationToken cancellationToken)
    {
        if (!mail.IsConfigured)
        {
            return ChangeAddressOutcome.NoMail;
        }

        string normalized;
        try
        {
            normalized = User.NormalizeEmailForComparison(email);
        }
        catch (ArgumentException)
        {
            return ChangeAddressOutcome.NotAnAddress;
        }

        if (!Password.TryRead(password, out var presented)
            || user.PasswordHash is null
            || hasher.Verify(user.PasswordHash, presented) is PasswordCheck.Wrong)
        {
            return ChangeAddressOutcome.PasswordRefused;
        }

        if (await identities.FindByEmailAsync(normalized, cancellationToken) is not null
            || await secrets.IsPendingAsync(normalized, cancellationToken))
        {
            return ChangeAddressOutcome.AddressTaken;
        }

        return await new SendALink(secrets, mail, templates, clock).ExecuteAsync(
            user, OneTimeSecretPurpose.EmailChange, cancellationToken, email)
            ? ChangeAddressOutcome.Sent
            : ChangeAddressOutcome.NotDelivered;
    }
}
