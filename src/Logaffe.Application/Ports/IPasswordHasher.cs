using Logaffe.Domain.Operators;

namespace Logaffe.Application.Ports;

/// <summary>
/// What a presented password turns out to be worth.
/// </summary>
public enum PasswordCheck
{
    /// <summary>
    /// Not the password. It says nothing further, and nothing above it acts on
    /// it beyond refusing: a wrong password never locks the account
    /// (ADR 0017).
    /// </summary>
    Wrong,

    /// <summary>The password, hashed at the parameters in use today.</summary>
    Right,

    /// <summary>
    /// The password, against a hash written by older parameters. It admits
    /// exactly as <see cref="Right"/> does — the difference is that the caller
    /// owes the row a rewrite at the current cost, which is what makes raising
    /// that cost later a path rather than an intention (ADR 0032).
    /// </summary>
    RightAndOutOfDate,
}

/// <summary>
/// What turns a password into the string a row holds, and says whether a
/// presented one matches it.
/// </summary>
/// <remarks>
/// <para>
/// It is a port because the algorithm is the part that gets replaced: today it
/// is the framework's PBKDF2-HMAC-SHA512, chosen for arriving with the shared
/// framework at no dependency cost, and a move to Argon2id later is this port
/// answered differently plus a verifier that reads one more format (ADR 0032).
/// The rule that does not move — how short a password may be — is
/// <see cref="Password"/> and lives in Domain.
/// </para>
/// <para>
/// It is also a port because <c>PasswordHasher&lt;T&gt;</c> lives in the ASP.NET
/// Core shared framework, which <c>Logaffe.Domain</c> does not have and must not
/// acquire (ADR 0030).
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
    /// than an exception. There is one account and no reset over the network, so
    /// an unreadable hash is the same event as a forgotten password and has the
    /// same answer: the host (ADR 0015).
    /// </remarks>
    PasswordCheck Verify(string storedHash, Password presented);
}
