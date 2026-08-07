using System.Security.Cryptography;
using System.Text;
using Logaffe.Application.Ports;

namespace Logaffe.Infrastructure.Secrets;

/// <summary>
/// AES-256-GCM under the key on the host volume.
/// </summary>
/// <remarks>
/// <para>
/// GCM authenticates as well as encrypts, so a row that was altered fails to
/// open rather than opening as something else. The nonce is drawn fresh for
/// every value, which is what keeps the encryption randomized — the property
/// ADR 0031 had to work around by giving a token an identifier, and the one a
/// deterministic scheme would have traded away.
/// </para>
/// <para>
/// A sealed value is <c>[version][nonce][tag][ciphertext]</c>. The version byte
/// costs one byte per sealed secret and is what lets the algorithm or the key
/// change later without having to guess what an existing row was written by.
/// </para>
/// </remarks>
public sealed class AesGcmSecretCipher(HostVolumeKey key) : ISecretCipher
{
    private const byte Version = 1;
    private const int NonceLength = 12;
    private const int TagLength = 16;
    private const int HeaderLength = 1 + NonceLength + TagLength;

    public byte[] Encrypt(string secret)
    {
        ArgumentNullException.ThrowIfNull(secret);

        var plaintext = Encoding.UTF8.GetBytes(secret);
        var sealedSecret = new byte[HeaderLength + plaintext.Length];

        sealedSecret[0] = Version;
        var nonce = sealedSecret.AsSpan(1, NonceLength);
        var tag = sealedSecret.AsSpan(1 + NonceLength, TagLength);
        var ciphertext = sealedSecret.AsSpan(HeaderLength);

        RandomNumberGenerator.Fill(nonce);

        using var aes = new AesGcm(key.Material, TagLength);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        CryptographicOperations.ZeroMemory(plaintext);

        return sealedSecret;
    }

    public string Decrypt(byte[] sealedSecret)
    {
        ArgumentNullException.ThrowIfNull(sealedSecret);

        if (sealedSecret.Length < HeaderLength || sealedSecret[0] != Version)
        {
            throw new CryptographicException(
                "This is not a value this cipher sealed.");
        }

        var nonce = sealedSecret.AsSpan(1, NonceLength);
        var tag = sealedSecret.AsSpan(1 + NonceLength, TagLength);
        var ciphertext = sealedSecret.AsSpan(HeaderLength);
        var plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(key.Material, TagLength);

        // Throws when the tag does not check out, which is the whole point of
        // GCM: a row that was altered, or the wrong key, fails here rather than
        // producing a secret that never matches and looks like an ordinary 401.
        aes.Decrypt(nonce, ciphertext, tag, plaintext);

        var secret = Encoding.UTF8.GetString(plaintext);
        CryptographicOperations.ZeroMemory(plaintext);

        return secret;
    }
}
