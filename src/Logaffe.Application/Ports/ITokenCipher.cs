namespace Logaffe.Application.Ports;

/// <summary>
/// What turns a token's secret into the bytes a row holds, and back.
/// </summary>
/// <remarks>
/// <para>
/// A token is stored encrypted rather than hashed so that the operator can read
/// one back instead of rotating and redeploying, and the key that makes it
/// readable lives on the host volume and never in the database — which is what
/// makes a stolen database backup yield nothing usable (ADR 0022).
/// </para>
/// <para>
/// The encryption is <em>randomized</em>: encrypting one secret twice gives two
/// different values, which is why a token cannot be found by its ciphertext and
/// carries an identifier naming its row instead (ADR 0031). Anything answering
/// this port that makes the ciphertext a function of the secret alone has
/// quietly reopened that decision.
/// </para>
/// </remarks>
public interface ITokenCipher
{
    /// <summary>
    /// Seals a token's secret. The result is what the row holds and is different
    /// every time, even for one secret.
    /// </summary>
    byte[] Encrypt(string secret);

    /// <summary>
    /// Opens what a row holds.
    /// </summary>
    /// <exception cref="System.Security.Cryptography.CryptographicException">
    /// The bytes are not something this cipher sealed, or not with the key it
    /// has. That is a corrupt row or a lost key — an installation-level fault
    /// rather than an answer about a presented token, which is refused long
    /// before this by not matching an identifier.
    /// </exception>
    string Decrypt(byte[] sealedSecret);
}
