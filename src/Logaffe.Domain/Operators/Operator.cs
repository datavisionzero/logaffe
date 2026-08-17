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
/// It carries the operator's secrets and stores them differently because what has
/// to be done with them differs (ADR 0032): the password is proved without being
/// held, and the second factor's secret is <em>encrypted</em>, because a code
/// cannot be computed without it. The backup codes are <see cref="BackupCode"/>,
/// which is a set rather than a field.
/// </para>
/// <para>
/// <b>The password is the only one of them it must have.</b> The second factor is
/// the operator's to enrol and to remove (ADR 0041), so an account that holds
/// none is an ordinary account rather than a half-built one — which is why the
/// claim takes a password and nothing else.
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

    private Operator(Guid id, string passwordHash, DateTimeOffset claimedAt)
    {
        Id = id;
        PasswordHash = passwordHash;
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
    /// <para>
    /// <c>null</c> on an account that has enrolled none, which is the state a
    /// claim leaves behind (ADR 0041).
    /// </para>
    /// </summary>
    public byte[]? EncryptedSecondFactorSecret { get; private set; }

    /// <summary>
    /// When the second factor became this one, and <c>null</c> while there is
    /// none. An enrolment over an existing one overwrites the secret and moves
    /// this date; nothing of the previous enrolment survives except the fact that
    /// there was one, which is the part of a history worth keeping (ADR 0032).
    /// </summary>
    public DateTimeOffset? SecondFactorEnrolledAt { get; private set; }

    /// <summary>
    /// Whether a code is going to be asked for at the next sign-in. It is the one
    /// question every path that touches the second factor starts from, and it is
    /// asked of the row rather than derived twice.
    /// </summary>
    public bool HasSecondFactor => EncryptedSecondFactorSecret is not null;

    public DateTimeOffset ClaimedAt { get; private init; }

    /// <summary>
    /// Establishes the account, which is the whole of the claim and the moment
    /// the installation stops being unclaimed.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The password was not hashed, which is a corrupt account rather than a
    /// claimable one.
    /// </exception>
    public static Operator Claim(string passwordHash, DateTimeOffset claimedAt) =>
        new(Guid.CreateVersion7(), Hashed(passwordHash), claimedAt);

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
    /// Takes a freshly enrolled second factor, whether or not there was one
    /// before — which is what makes enrolling and replacing a phone one act
    /// (ADR 0016, ADR 0041).
    /// </summary>
    /// <remarks>
    /// An overwrite, not a second enrolment beside the first: a previous secret
    /// is gone the moment this returns, and what is kept of it is the date it
    /// stopped being current. Issuing the sheet of backup codes that goes with an
    /// enrolment is the caller's, because they are their own rows.
    /// </remarks>
    public void EnrolSecondFactor(byte[] encryptedSecondFactorSecret, DateTimeOffset at)
    {
        EncryptedSecondFactorSecret = Sealed(encryptedSecondFactorSecret);
        SecondFactorEnrolledAt = at;
    }

    /// <summary>
    /// Removes the second factor, leaving an account behind a password alone.
    /// </summary>
    /// <remarks>
    /// It is the operator's decision to make and it costs what enrolling costs —
    /// the password and a current code — so that a session somebody else took
    /// cannot strip the account down (ADR 0041). The backup codes go with it,
    /// because a code that stands in for a second factor that is not there stands
    /// in for nothing; they are rows, so removing them is the caller's.
    /// </remarks>
    public void RemoveSecondFactor()
    {
        EncryptedSecondFactorSecret = null;
        SecondFactorEnrolledAt = null;
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
