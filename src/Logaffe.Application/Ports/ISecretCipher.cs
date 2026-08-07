namespace Logaffe.Application.Ports;

/// <summary>
/// What turns a secret into the bytes a row holds, and back, under the key on
/// the host volume.
/// </summary>
/// <remarks>
/// <para>
/// A token is stored encrypted rather than hashed so that the operator can read
/// one back instead of rotating and redeploying, and the key that makes it
/// readable lives on the host volume and never in the database — which is what
/// makes a stolen database backup yield nothing usable (ADR 0022). The
/// operator's TOTP secret is sealed the same way for a different reason: a code
/// cannot be computed without it (ADR 0032).
/// </para>
/// <para>
/// Those are the two callers, and they are one port rather than two because
/// there is one key, one algorithm and one thing being asked. What the port is
/// <em>not</em> is the storage of every secret in the product: a password and a
/// backup code are hashed and never arrive here, and anything that shows up
/// wanting to be sealed has to be a thing the installation must be able to read
/// again.
/// </para>
/// <para>
/// The encryption is <em>randomized</em>: encrypting one secret twice gives two
/// different values, which is why a token cannot be found by its ciphertext and
/// carries an identifier naming its row instead (ADR 0031). Anything answering
/// this port that makes the ciphertext a function of the secret alone has
/// quietly reopened that decision.
/// </para>
/// </remarks>
public interface ISecretCipher
{
    /// <summary>
    /// Seals a secret. The result is what the row holds and is different every
    /// time, even for one secret.
    /// </summary>
    byte[] Encrypt(string secret);

    /// <summary>
    /// Opens what a row holds.
    /// </summary>
    /// <exception cref="System.Security.Cryptography.CryptographicException">
    /// The bytes are not something this cipher sealed, or not with the key it
    /// has. That is a corrupt row or a lost key — an installation-level fault
    /// rather than an answer about a presented credential, which is refused long
    /// before this by not matching an identifier.
    /// </exception>
    string Decrypt(byte[] sealedSecret);
}
