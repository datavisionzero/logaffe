using System.Globalization;
using System.Net.Mail;
using System.Text;

namespace Logaffe.Domain.Identities;

/// <summary>
/// Where a user's account stands. There is no fourth state and no deletion
/// (ADR 0052).
/// </summary>
public enum UserState
{
    /// <summary>
    /// Invited and not yet arrived: an address and no password. Redeeming the
    /// invitation sets the first password and makes the account
    /// <see cref="Active"/>.
    /// </summary>
    Invited,

    /// <summary>Signs in, holds whatever projects were assigned.</summary>
    Active,

    /// <summary>
    /// Closed. Cannot sign in, and its agents cannot authenticate. Everything
    /// that points at it still points at something, which is why this is not a
    /// deletion.
    /// </summary>
    Deactivated,
}

/// <summary>
/// A person's account: an address to sign in with, a password, a state, and the
/// two optional secrets of a second factor (<c>CONTEXT.md</c>, User).
/// </summary>
/// <remarks>
/// <para>
/// The three secrets are stored for what they are and each differently
/// (ADR 0057): the password is proved without being held, a backup code is
/// hashed once and never recoverable, and the TOTP secret is encrypted under the
/// key on the host volume because a code cannot be computed without it.
/// </para>
/// <para>
/// <b>The password is the only one it must have, and even that only once the
/// account is active.</b> An invited user has none until they set one, and the
/// second factor is theirs to enrol and to remove (ADR 0041), so an account
/// holding none is an ordinary account rather than a half-built one.
/// </para>
/// </remarks>
public sealed class User : Identity
{
    /// <summary>
    /// What the column has to hold. Argon2id writes a PHC string of about a
    /// hundred characters; the room above that is for the next set of
    /// parameters, which is the whole point of encoding them in the value
    /// (ADR 0057).
    /// </summary>
    public const int PasswordHashMaxLength = 256;

    /// <summary>
    /// What an address may be. Long enough for anything deliverable and short
    /// enough to be an index rather than a page of text.
    /// </summary>
    public const int EmailMaxLength = 254;

    private User()
    {
        // EF Core materializes through this; every other route goes through
        // Create or Invite.
    }

    private User(
        Guid id,
        string name,
        string email,
        UserState state,
        bool administrator,
        DateTimeOffset createdAt)
        : base(id, name, administrator, createdAt)
    {
        Email = NormalizeEmail(email);
        NormalizedEmail = NormalizeEmailForComparison(email);
        State = state;
    }

    public override IdentityKind Kind => IdentityKind.User;

    /// <summary>
    /// The address as it was written, kept for display. Nothing is ever looked
    /// up by this.
    /// </summary>
    public string Email { get; private set; } = null!;

    /// <summary>
    /// The address folded to what two spellings of it have in common, which is
    /// what everything looks up by and what the unique index stands on
    /// (ADR 0052).
    /// </summary>
    public string NormalizedEmail { get; private set; } = null!;

    public UserState State { get; private set; }

    /// <summary>
    /// What the hasher wrote, carrying its own parameters, and the only form of
    /// the password that exists anywhere. <c>null</c> on an invited account that
    /// has not set one yet.
    /// </summary>
    public string? PasswordHash { get; private set; }

    /// <summary>
    /// The TOTP secret under the key on the host volume — encrypted rather than
    /// hashed, because a code cannot be computed without it (ADR 0057).
    /// <c>null</c> on an account that has enrolled none.
    /// </summary>
    public byte[]? EncryptedSecondFactorSecret { get; private set; }

    /// <summary>
    /// When the second factor became this one. An enrolment over an existing one
    /// overwrites the secret and moves this date; nothing of the previous
    /// enrolment survives except the fact that there was one.
    /// </summary>
    public DateTimeOffset? SecondFactorEnrolledAt { get; private set; }

    /// <summary>
    /// Whether a code is going to be asked for at the next sign-in. Every path
    /// that touches the second factor starts from this question, so it is asked
    /// of the row rather than derived twice.
    /// </summary>
    public bool HasSecondFactor => EncryptedSecondFactorSecret is not null;

    /// <summary>Whether this account may sign in and act at all.</summary>
    public bool IsActive => State is UserState.Active;

    /// <summary>
    /// The first administrator, created out of the configuration on the one
    /// start where the installation holds no identity (ADR 0054).
    /// </summary>
    /// <remarks>
    /// Invited rather than active, and for once that is not a formality: it is
    /// what makes the bootstrap token single-use without storing it anywhere.
    /// The exchange gives this account its password and activates it, and from
    /// that moment there is no passwordless administrator for a token to be
    /// exchanged against.
    /// </remarks>
    public static User Bootstrap(string name, string email, DateTimeOffset createdAt) =>
        new(Guid.CreateVersion7(), name, email, UserState.Invited, administrator: true, createdAt);

    /// <summary>
    /// A user an administrator invited. They begin without a password and
    /// without any project access at all (ADR 0055).
    /// </summary>
    public static User Invite(
        string name, string email, bool administrator, DateTimeOffset createdAt) =>
        new(Guid.CreateVersion7(), name, email, UserState.Invited, administrator, createdAt);

    /// <summary>
    /// Takes the password this user just chose, which is their own act: it
    /// requires what they already hold, and it ends every other session of
    /// theirs — the last of which is the sessions' business and not this row's.
    /// </summary>
    public void ChangePasswordTo(string passwordHash) => PasswordHash = Hashed(passwordHash);

    /// <summary>
    /// Takes the same password again at the current cost, after a sign-in proved
    /// it against a hash written by older parameters.
    /// </summary>
    /// <remarks>
    /// This is <see cref="ChangePasswordTo"/>'s twin and deliberately not the
    /// same method: one of them is something the user did and ends their other
    /// sessions, the other is maintenance nobody asked for and must not. It is
    /// what carries an installation from PBKDF2 to Argon2id without anybody
    /// resetting anything (ADR 0057).
    /// </remarks>
    public void RehashedTo(string passwordHash) => PasswordHash = Hashed(passwordHash);

    /// <summary>
    /// Sets the first password of an invited account, which is what an
    /// invitation is redeemed for, and makes it active in the same act.
    /// </summary>
    public void ActivateWith(string passwordHash)
    {
        PasswordHash = Hashed(passwordHash);
        State = UserState.Active;
    }

    /// <summary>
    /// Closes the account. Its sessions and its agents' tokens stop admitting
    /// anything, which is the callers' to carry out because those are rows of
    /// their own.
    /// </summary>
    public void Deactivate() => State = UserState.Deactivated;

    /// <summary>
    /// Opens it again. An account that was invited and never arrived goes back
    /// to <see cref="UserState.Invited"/> rather than becoming active without a
    /// password.
    /// </summary>
    public void Reactivate() =>
        State = PasswordHash is null ? UserState.Invited : UserState.Active;

    public void ChangeAdministratorRole(bool administrator) => Administrator = administrator;

    /// <summary>
    /// Takes the new address, which happens at the moment a change is redeemed
    /// and not when it is requested — until then the old address is still the
    /// one that signs in (ADR 0053).
    /// </summary>
    public void ChangeEmailTo(string email)
    {
        Email = NormalizeEmail(email);
        NormalizedEmail = NormalizeEmailForComparison(email);
    }

    /// <summary>
    /// Takes a freshly enrolled second factor, whether or not there was one
    /// before — which is what makes enrolling and replacing a phone one act
    /// (ADR 0016, ADR 0041).
    /// </summary>
    public void EnrolSecondFactor(byte[] encryptedSecondFactorSecret, DateTimeOffset at)
    {
        EncryptedSecondFactorSecret = Sealed(encryptedSecondFactorSecret);
        SecondFactorEnrolledAt = at;
    }

    /// <summary>
    /// Removes the second factor, leaving an account behind a password alone. It
    /// costs what enrolling costs, so that a session somebody took cannot strip
    /// the account down (ADR 0041).
    /// </summary>
    public void RemoveSecondFactor()
    {
        EncryptedSecondFactorSecret = null;
        SecondFactorEnrolledAt = null;
    }

    /// <summary>
    /// The address as it will be stored for display: trimmed, Unicode-normalized
    /// and checked for being an address at all.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Not an address, or longer than <see cref="EmailMaxLength"/>.
    /// </exception>
    public static string NormalizeEmail(string? email)
    {
        var trimmed = email?.Trim().Normalize(NormalizationForm.FormKC);

        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > EmailMaxLength)
        {
            throw new ArgumentException(
                $"An email address is required and is at most {EmailMaxLength} characters.",
                nameof(email));
        }

        // MailAddress accepts a display name in front of an address, which this
        // must not: comparing what it parsed against what arrived is what
        // refuses `Someone <a@b.c>` as a sign-in address.
        return MailAddress.TryCreate(trimmed, out var parsed)
            && string.Equals(parsed.Address, trimmed, StringComparison.OrdinalIgnoreCase)
                ? trimmed
                : throw new ArgumentException(
                    "An email address is required.", nameof(email));
    }

    /// <summary>
    /// The address folded for comparison, which is what is looked up and what
    /// the unique index holds. Lowercased invariantly, so that the answer does
    /// not depend on the culture the process happens to run under.
    /// </summary>
    public static string NormalizeEmailForComparison(string? email) =>
        NormalizeEmail(email).ToLower(CultureInfo.InvariantCulture);

    private static string Hashed(string? passwordHash) =>
        string.IsNullOrWhiteSpace(passwordHash)
            ? throw new ArgumentException(
                "A user holds their password hashed.", nameof(passwordHash))
            : passwordHash.Length <= PasswordHashMaxLength
                ? passwordHash
                : throw new ArgumentException(
                    $"A password hash is at most {PasswordHashMaxLength} characters.",
                    nameof(passwordHash));

    private static byte[] Sealed(byte[]? encryptedSecondFactorSecret) =>
        encryptedSecondFactorSecret is { Length: > 0 }
            ? encryptedSecondFactorSecret
            : throw new ArgumentException(
                "A user holds their second factor's secret encrypted.",
                nameof(encryptedSecondFactorSecret));
}
