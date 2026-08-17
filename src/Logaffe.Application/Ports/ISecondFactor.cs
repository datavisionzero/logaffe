namespace Logaffe.Application.Ports;

/// <summary>
/// What draws the secret an authenticator app is enrolled with, and says whether
/// a presented code is one that secret produces now.
/// </summary>
/// <remarks>
/// <para>
/// The second factor is a time-based one-time code, it is enrolled during the
/// claim, it can be re-enrolled by a signed-in operator, and it cannot be turned
/// off (ADR 0016). None of that is here: this port is the arithmetic, and the
/// acts that call it are use cases.
/// </para>
/// <para>
/// The secret this mints is handed straight to <see cref="ISecretCipher"/> and
/// stored sealed, because a code cannot be computed without it (ADR 0032) — so
/// unlike the operator's other two credentials it is not hashed, and unlike them
/// it is unusable if the key on the host volume is lost.
/// </para>
/// </remarks>
public interface ISecondFactor
{
    /// <summary>
    /// Draws a secret for a fresh enrolment, in the text form an authenticator
    /// app reads. It is what the operator types by hand when they cannot scan
    /// the code, so it is the adapter's business to keep it typable.
    /// </summary>
    string MintSecret();

    /// <summary>
    /// Whether <paramref name="code"/> is a code <paramref name="secret"/>
    /// produces around <paramref name="at"/>. Anything that is not six digits is
    /// simply not a code and is refused as one.
    /// </summary>
    /// <remarks>
    /// The tolerance for a phone whose clock is a little out is the adapter's,
    /// and it is deliberately narrow: every step of slack is a step an attacker
    /// gets to guess in as well.
    /// </remarks>
    bool Verifies(string secret, string? code, DateTimeOffset at);

    /// <summary>
    /// The <c>otpauth:</c> address an authenticator app is enrolled from, which
    /// is what the QR code shown at an enrolment carries.
    /// </summary>
    /// <param name="secret">The secret just minted.</param>
    /// <param name="account">
    /// What the app should call this installation in its list. There is no
    /// username to put here (ADR 0015), so it is the installation's address —
    /// which the adapter above knows and this one does not.
    /// </param>
    string EnrolmentUri(string secret, string account);
}
