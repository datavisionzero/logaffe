using Logaffe.Domain.Identities;

namespace Logaffe.Application.Ports;

/// <summary>
/// What a presented password turns out to be worth.
/// </summary>
public enum PasswordCheck
{
    /// <summary>
    /// Not the password. It says nothing further; what an accumulation of these
    /// costs is decided by the throttle and not here (ADR 0056).
    /// </summary>
    Wrong,

    /// <summary>The password, hashed at the parameters in use today.</summary>
    Right,

    /// <summary>
    /// The password, against a hash written by older parameters — a different
    /// algorithm included. It admits exactly as <see cref="Right"/> does; the
    /// difference is that the caller owes the row a rewrite at the current cost,
    /// which is what carries an installation from PBKDF2 to Argon2id without
    /// anybody resetting anything (ADR 0057).
    /// </summary>
    RightAndOutOfDate,
}

/// <summary>
/// What turns a password into the string a row holds, and says whether a
/// presented one matches it.
/// </summary>
/// <remarks>
/// <para>
/// It is a port because the algorithm is the part that gets replaced, and it has
/// been: it was the framework's PBKDF2-HMAC-SHA512 and it is Argon2id, with a
/// verifier that still reads the older format so that nobody is locked out
/// (ADR 0057). The rule that does not move — how short a password may be — is
/// <see cref="Password"/> and lives in Domain.
/// </para>
/// <para>
/// It is also a port because the hashing packages live outside
/// <c>Logaffe.Domain</c>, which carries no package references at all
/// (ADR 0030).
/// </para>
/// </remarks>
public interface IPasswordHasher
{
    /// <summary>
    /// Hashes at the parameters in use today, writing the marker that says which
    /// those were.
    /// </summary>
    string Hash(Password password);

    /// <summary>
    /// Whether <paramref name="presented"/> is the password behind
    /// <paramref name="storedHash"/>, and whether that hash is still current.
    /// </summary>
    /// <remarks>
    /// A stored hash this port cannot read — a format from a version that is not
    /// this one, or a corrupt row — is <see cref="PasswordCheck.Wrong"/> rather
    /// than an exception. An unreadable hash is then the same event as a
    /// forgotten password and has the same answer: recovery by mail, or the host
    /// when that is gone too.
    /// </remarks>
    PasswordCheck Verify(string storedHash, Password presented);
}
