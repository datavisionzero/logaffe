namespace Logaffe.Domain.Operators;

/// <summary>
/// The single human account of an installation, which can see and do everything.
/// </summary>
/// <remarks>
/// <para>
/// There is exactly one — no username, no email address, nothing identifying
/// which of one is meant (ADR 0015) — so this row is the whole of the product's
/// notion of a person. It comes into being with the claim and leaves with Host
/// Recovery, which removes it rather than resetting anything
/// (ADR 0013).
/// </para>
/// <para>
/// It carries two of the operator's three secrets and stores them differently
/// because what has to be done with them differs (ADR 0032): the password is
/// proved without being held, and the second factor's secret is
/// <em>encrypted</em>, because a code cannot be computed without it. The third —
/// the backup codes — is <see cref="BackupCode"/>, which is a set rather than a
/// field.
/// </para>
/// <para>
/// The claim is atomic and holds nothing (ADR 0014): an instance of this exists
/// only when every part of it does, which is why there is one factory taking all
/// of them and no way to build a half-claimed operator.
/// </para>
/// </remarks>
public sealed class Operator
{
    /// <summary>
    /// What the column has to hold. The framework's PBKDF2 writes about eighty
    /// characters of base64 with a version marker in front of it; the room above
    /// that is for the next format, which is the whole point of the marker
    /// (ADR 0032).
    /// </summary>
    public const int PasswordHashMaxLength = 256;

    private Operator()
    {
        // EF Core materializes through this; every other route goes through Claim.
    }

    private Operator(
        Guid id,
        string passwordHash,
        byte[] encryptedSecondFactorSecret,
        DateTimeOffset claimedAt)
    {
        Id = id;
        PasswordHash = passwordHash;
        EncryptedSecondFactorSecret = encryptedSecondFactorSecret;
        SecondFactorEnrolledAt = claimedAt;
        ClaimedAt = claimedAt;
    }

    public Guid Id { get; private init; }

    /// <summary>
    /// What the hasher wrote, carrying its own version marker, and the only form
    /// of the password that exists anywhere. It is rewritten at the current cost
    /// whenever a sign-in succeeds, which is what makes raising that cost later a
    /// path rather than an intention.
    /// </summary>
    public string PasswordHash { get; private set; } = null!;

    /// <summary>
    /// The TOTP secret under the key that lives on the host volume and never in
    /// the database — encrypted rather than hashed, because a code cannot be
    /// computed without it (ADR 0032). An installation restored without its key
    /// cannot verify a code at all, and the backup codes are then the only way
    /// in.
    /// </summary>
    public byte[] EncryptedSecondFactorSecret { get; private set; } = null!;

    /// <summary>
    /// When the second factor last became this one. A re-enrolment overwrites
    /// the secret and moves this date; nothing of the previous enrolment
    /// survives except the fact that there was one, which is the part of a
    /// history worth keeping (ADR 0032).
    /// </summary>
    public DateTimeOffset SecondFactorEnrolledAt { get; private set; }

    public DateTimeOffset ClaimedAt { get; private init; }

    /// <summary>
    /// Establishes the account, which is the last step of the claim and the
    /// moment the installation stops being unclaimed.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The password was not hashed, or the second factor's secret was not
    /// sealed. Either is a corrupt account rather than a claimable one.
    /// </exception>
    public static Operator Claim(
        string passwordHash,
        byte[] encryptedSecondFactorSecret,
        DateTimeOffset claimedAt) =>
        new(
            Guid.CreateVersion7(),
            Hashed(passwordHash),
            Sealed(encryptedSecondFactorSecret),
            claimedAt);

    /// <summary>
    /// Takes the password the operator just chose. It is the operator's own act,
    /// it requires the current password, and it ends every other session — the
    /// last of which is the sessions' business and not this row's.
    /// </summary>
    public void ChangePasswordTo(string passwordHash) => PasswordHash = Hashed(passwordHash);

    /// <summary>
    /// Takes the same password again at the current cost, after a sign-in proved
    /// it against a hash written by older parameters.
    /// </summary>
    /// <remarks>
    /// This is <see cref="ChangePasswordTo"/>'s twin and deliberately not the
    /// same method: one of them is something the operator did and ends their
    /// other sessions, the other is maintenance nobody asked for and must not.
    /// </remarks>
    public void RehashedTo(string passwordHash) => PasswordHash = Hashed(passwordHash);

    /// <summary>
    /// Replaces the second factor with a freshly enrolled one, which is what
    /// makes a replaced phone an ordinary afternoon (ADR 0016).
    /// </summary>
    /// <remarks>
    /// An overwrite, not a second enrolment beside the first: the previous
    /// secret is gone the moment this returns, and what is kept of it is the
    /// date it stopped being current. Issuing the fresh set of backup codes that
    /// goes with a re-enrolment is the caller's, because they are their own
    /// rows.
    /// </remarks>
    public void ReEnrolSecondFactor(byte[] encryptedSecondFactorSecret, DateTimeOffset at)
    {
        EncryptedSecondFactorSecret = Sealed(encryptedSecondFactorSecret);
        SecondFactorEnrolledAt = at;
    }

    private static string Hashed(string? passwordHash) =>
        string.IsNullOrWhiteSpace(passwordHash)
            ? throw new ArgumentException(
                "An operator holds their password hashed.", nameof(passwordHash))
            : passwordHash.Length <= PasswordHashMaxLength
                ? passwordHash
                : throw new ArgumentException(
                    $"A password hash is at most {PasswordHashMaxLength} characters.",
                    nameof(passwordHash));

    private static byte[] Sealed(byte[]? encryptedSecondFactorSecret) =>
        encryptedSecondFactorSecret is { Length: > 0 }
            ? encryptedSecondFactorSecret
            : throw new ArgumentException(
                "An operator holds their second factor's secret encrypted.",
                nameof(encryptedSecondFactorSecret));
}
