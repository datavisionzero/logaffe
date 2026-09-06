using System.Buffers.Text;
using System.Security.Cryptography;

namespace Logaffe.Domain.Identities;

/// <summary>
/// The three things a one-time secret is ever for (ADR 0053).
/// </summary>
/// <remarks>
/// One table for three purposes rather than three tables: what differs between
/// them is a lifetime and a sentence in a message, and everything that makes
/// them safe — the hash, the expiry, the single use, the one live secret per
/// purpose — is the same in all three.
/// </remarks>
public enum OneTimeSecretPurpose
{
    /// <summary>
    /// An administrator asked somebody to join. Redeeming it sets their first
    /// password and makes the account active.
    /// </summary>
    Invitation,

    /// <summary>
    /// Somebody asked for their password back. Redeeming it replaces the
    /// password and ends every session that account had.
    /// </summary>
    PasswordRecovery,

    /// <summary>
    /// Somebody is moving to another address. It carries the address being moved
    /// to, and nothing changes until it is redeemed.
    /// </summary>
    EmailChange,
}

/// <summary>
/// A value sent to an address so that its holder may do exactly one thing
/// (ADR 0053).
/// </summary>
/// <remarks>
/// <para>
/// <b>Only the hash is kept.</b> What went out in the link exists for the length
/// of one request, the way a session secret and a backup code do
/// (ADR 0057) — a store that could reproduce the link would be a store that
/// could take somebody's account without their password.
/// </para>
/// <para>
/// <b>A newer one of the same purpose spends the one before it.</b> A person who
/// asks twice because the first message went to spam holds two links and expects
/// the newer one to work; two live links are two chances for the older one to be
/// found in a mailbox later. The unique index over the user and the purpose,
/// while the secret is unspent, is what actually holds that.
/// </para>
/// <para>
/// <b>Redeeming is a row lock and a timestamp.</b> Two requests arriving with the
/// same link must not both win, and the one that reads it second finds it spent.
/// </para>
/// </remarks>
public sealed class OneTimeSecret
{
    /// <summary>
    /// Two hundred and fifty-six bits, drawn from a cryptographic source and
    /// written into a link. Long enough that guessing is not a strategy, which
    /// is why nothing here is rate-limited beyond the ordinary.
    /// </summary>
    public const int SecretBytes = 32;

    /// <summary>SHA-256, so every stored hash is this long.</summary>
    public const int HashLength = 32;

    /// <summary>
    /// How long an invitation lives. Long enough to survive a weekend and a
    /// spam folder, short enough that a link forwarded to somebody in a mailbox
    /// somewhere stops working.
    /// </summary>
    public static readonly TimeSpan InvitationLifetime = TimeSpan.FromDays(7);

    /// <summary>
    /// How long the other two live. They are answers to something somebody is
    /// doing right now, so the window is the length of a coffee rather than a
    /// week.
    /// </summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(1);

    private OneTimeSecret()
    {
        // EF Core materializes through this; every other route goes through Issue.
    }

    private OneTimeSecret(
        Guid userId,
        OneTimeSecretPurpose purpose,
        byte[] secretHash,
        string? pendingEmail,
        DateTimeOffset issuedAt)
    {
        Id = Guid.CreateVersion7();
        UserId = userId;
        Purpose = purpose;
        SecretHash = secretHash;
        PendingEmail = pendingEmail is null ? null : User.NormalizeEmail(pendingEmail);
        PendingNormalizedEmail =
            pendingEmail is null ? null : User.NormalizeEmailForComparison(pendingEmail);
        IssuedAt = issuedAt;
        ExpiresAt = issuedAt + (purpose is OneTimeSecretPurpose.Invitation
            ? InvitationLifetime
            : Lifetime);
    }

    public Guid Id { get; private init; }

    public Guid UserId { get; private init; }

    public OneTimeSecretPurpose Purpose { get; private init; }

    /// <inheritdoc cref="Hash"/>
    public byte[] SecretHash { get; private init; } = null!;

    /// <summary>
    /// The address being moved to, on an <see cref="OneTimeSecretPurpose.EmailChange"/>
    /// and on nothing else. Until this is redeemed the old address is still the
    /// one that signs in, which is what makes a change that is never confirmed
    /// cost nothing.
    /// </summary>
    public string? PendingEmail { get; private init; }

    /// <summary>
    /// The folded form of <see cref="PendingEmail"/>, which is what a reservation
    /// against other users and other live changes is checked on.
    /// </summary>
    public string? PendingNormalizedEmail { get; private init; }

    public DateTimeOffset IssuedAt { get; private init; }

    public DateTimeOffset ExpiresAt { get; private init; }

    /// <summary>
    /// When it was spent — by being redeemed, or by a newer one of the same
    /// purpose replacing it. The two are one column deliberately: what both
    /// mean is that this link no longer opens anything.
    /// </summary>
    public DateTimeOffset? UsedAt { get; private set; }

    /// <summary>Whether this still opens what it was issued for.</summary>
    public bool IsLive(DateTimeOffset now) => UsedAt is null && ExpiresAt > now;

    /// <summary>
    /// Draws one: what goes into the link, and what the installation keeps of
    /// it. They are returned together because they are one act — there is no
    /// moment at which either half is meaningful on its own.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// A pending address was given for a purpose that carries none, or withheld
    /// from the one that must.
    /// </exception>
    public static Issued Issue(
        Guid userId,
        OneTimeSecretPurpose purpose,
        DateTimeOffset issuedAt,
        string? pendingEmail = null)
    {
        if (purpose is OneTimeSecretPurpose.EmailChange != pendingEmail is not null)
        {
            throw new ArgumentException(
                "Only a change of address carries the address being moved to.",
                nameof(pendingEmail));
        }

        var drawn = RandomNumberGenerator.GetBytes(SecretBytes);

        return new Issued(
            new OneTimeSecret(userId, purpose, SHA256.HashData(drawn), pendingEmail, issuedAt),
            Base64Url.EncodeToString(drawn));
    }

    /// <summary>
    /// What a presented link's secret hashes to, or <c>null</c> when it is not
    /// one of these at all.
    /// </summary>
    /// <remarks>
    /// The wrong length or a character outside base64url is refused here, before
    /// the table is read: a value that was never a secret does not deserve a
    /// lookup.
    /// </remarks>
    public static byte[]? Hash(string? presented)
    {
        if (string.IsNullOrWhiteSpace(presented))
        {
            return null;
        }

        Span<byte> drawn = stackalloc byte[SecretBytes];

        return Base64Url.TryDecodeFromChars(presented.Trim(), drawn, out var written)
            && written == SecretBytes
                ? SHA256.HashData(drawn)
                : null;
    }

    /// <summary>
    /// Spends it, which is what redeeming does.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// It was already spent or has expired. Callers ask <see cref="IsLive"/>
    /// first; this is what stops two requests carrying one link from both
    /// winning.
    /// </exception>
    public void ConsumeAt(DateTimeOffset now) =>
        UsedAt = IsLive(now)
            ? now
            : throw new InvalidOperationException("This secret no longer opens anything.");

    /// <summary>
    /// Spends it because a newer one of the same purpose was issued. Unlike
    /// <see cref="ConsumeAt"/> it is not an error to call on one that is already
    /// spent: replacing is about the state afterwards.
    /// </summary>
    public void ReplacedAt(DateTimeOffset now) => UsedAt ??= now;
}

/// <summary>
/// A secret as it was drawn: the row to write, and the value that goes into the
/// link and exists nowhere else.
/// </summary>
public sealed record Issued(OneTimeSecret Secret, string Value);
