using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Logaffe.IntegrationTests;

/// <summary>
/// The authenticator app, as far as these tests need one: it reads a secret in
/// the form the enrolment hands it over in and says what the six digits are now.
/// </summary>
/// <remarks>
/// Written out here rather than reached for in <c>Logaffe.Infrastructure</c> on
/// purpose. A test that computed its codes with the same code the installation
/// verifies them with would agree with itself about a wrong answer; this is RFC
/// 6238 read off the RFC, which is what the phone in the operator's pocket also
/// is.
/// </remarks>
public static class Authenticator
{
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    /// <summary>What an app enrolled with <paramref name="secret"/> shows now.</summary>
    public static string CodeFor(string secret, DateTimeOffset? at = null)
    {
        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(
            counter, (at ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds() / 30);

        var mac = HMACSHA1.HashData(Decode(secret), counter);
        var offset = mac[^1] & 0x0F;
        var truncated = BinaryPrimitives.ReadUInt32BigEndian(mac.AsSpan(offset, 4)) & 0x7FFFFFFF;

        return (truncated % 1_000_000).ToString("D6");
    }

    /// <summary>RFC 4648 base32 without padding, which is what an app is enrolled from.</summary>
    private static byte[] Decode(string secret)
    {
        var octets = new List<byte>(secret.Length * 5 / 8);
        var buffer = 0;
        var bits = 0;

        foreach (var symbol in secret.Where(character => character is not ('=' or ' ')))
        {
            buffer = (buffer << 5) | Base32Alphabet.IndexOf(char.ToUpperInvariant(symbol));
            bits += 5;

            if (bits >= 8)
            {
                bits -= 8;
                octets.Add((byte)((buffer >> bits) & 0xFF));
            }
        }

        return [.. octets];
    }
}
