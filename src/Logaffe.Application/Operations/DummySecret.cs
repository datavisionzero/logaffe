using Logaffe.Application.Ports;
using Logaffe.Domain.Tokens;

namespace Logaffe.Application.Operations;

/// <summary>
/// A sealed secret belonging to no token, which is what an identifier naming no
/// row is refused against.
/// </summary>
/// <remarks>
/// <para>
/// ADR 0031 requires an identifier that matches nothing and a secret that
/// mismatches to cost the same, so that the <c>401</c> of
/// <c>docs/ingestion.md</c> stays as silent about which it was as it is about
/// everything else. A lookup that finds nothing therefore still decrypts
/// something and still compares it, and this is the something.
/// </para>
/// <para>
/// It is sealed once and held for the life of the process. Sealing one per
/// request would make the miss the more expensive of the two cases and hand
/// back, as an encryption, the difference this exists to remove.
/// </para>
/// </remarks>
public sealed class DummySecret(ISecretCipher cipher)
{
    private readonly Lazy<byte[]> sealedSecret = new(
        () => cipher.Encrypt(TokenAlphabet.Random(TokenText.SecretLength)),
        LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// The bytes, in the shape a token row holds: drawn from the same alphabet
    /// at the same length and sealed by the same cipher, because a value that
    /// decrypts more cheaply than a real one would defeat the purpose.
    /// </summary>
    public byte[] Sealed => sealedSecret.Value;
}
