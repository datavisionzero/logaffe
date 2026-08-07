using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Logaffe.Application.Ports;

namespace Logaffe.Infrastructure.Secrets;

/// <summary>
/// TOTP as RFC 6238 defines it and as every authenticator app implements it:
/// HMAC-SHA1 over a thirty-second counter, truncated to six digits.
/// </summary>
/// <remarks>
/// <para>
/// The parameters are the defaults rather than choices. SHA-1 here is a
/// one-time code with a thirty-second life, not a signature — and an app that
/// cannot be enrolled is a second factor nobody has, which is the risk that
/// actually matters (ADR 0016).
/// </para>
/// <para>
/// Nothing here is stored and nothing here holds state. The secret arrives from
/// the caller, which read it out of the account row and opened it with the key
/// on the host volume (ADR 0032).
/// </para>
/// </remarks>
public sealed class Rfc6238SecondFactor : ISecondFactor
{
    /// <summary>
    /// A hundred and sixty bits, which is HMAC-SHA1's own size and what the
    /// apps expect. Written out in base32 it is thirty-two characters — the line
    /// under the QR code for an operator with a camera that will not focus.
    /// </summary>
    public const int SecretLengthInBytes = 20;

    public const int Digits = 6;

    /// <summary>What the truncated hash is taken modulo to leave <see cref="Digits"/> of it.</summary>
    private const uint DigitCeiling = 1_000_000;

    public static readonly TimeSpan Step = TimeSpan.FromSeconds(30);

    /// <summary>
    /// One step either side, so a phone whose clock is half a minute out still
    /// works. It is deliberately not more: every step of slack is another code
    /// that is valid at any moment, and a code is only six digits.
    /// </summary>
    private const int StepsOfSlack = 1;

    public string MintSecret() =>
        Base32.Encode(RandomNumberGenerator.GetBytes(SecretLengthInBytes));

    public bool Verifies(string secret, string? code, DateTimeOffset at)
    {
        if (code is null || code.Length != Digits || !code.All(char.IsAsciiDigit))
        {
            return false;
        }

        if (!Base32.TryDecode(secret, out var key) || key.Length == 0)
        {
            return false;
        }

        // Floored rather than truncated, so that the steps stay the same width
        // on both sides of the epoch. Nothing verifies a code from 1969, but a
        // division that changes direction at zero is the kind of thing that is
        // found later and somewhere else.
        var step = (long)Math.Floor(at.ToUnixTimeSeconds() / Step.TotalSeconds);
        var presented = Encoding.UTF8.GetBytes(code);

        // Every step in the window is computed and compared, and the loop does
        // not stop at the one that matched. A comparison that returned early
        // would say which step a code belonged to, and a wrong code that stopped
        // sooner than a right one would say more than that.
        var matched = false;
        for (var offset = -StepsOfSlack; offset <= StepsOfSlack; offset++)
        {
            matched |= CryptographicOperations.FixedTimeEquals(
                presented, Encoding.UTF8.GetBytes(CodeFor(key, step + offset)));
        }

        CryptographicOperations.ZeroMemory(key);

        return matched;
    }

    public string EnrolmentUri(string secret, string account)
    {
        // The label is `issuer:account` and the issuer is repeated as a
        // parameter, which is what the apps that show a name in their list
        // actually read.
        var label = Uri.EscapeDataString($"logaffe:{account}");

        return $"otpauth://totp/{label}"
            + $"?secret={secret}"
            + "&issuer=logaffe"
            + "&algorithm=SHA1"
            + $"&digits={Digits}"
            + $"&period={(int)Step.TotalSeconds}";
    }

    /// <summary>
    /// The code one counter step produces: an HMAC of the step, the dynamic
    /// truncation RFC 4226 describes, and the last six digits of what is left.
    /// </summary>
    private static string CodeFor(byte[] key, long step)
    {
        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, step);

        Span<byte> mac = stackalloc byte[HMACSHA1.HashSizeInBytes];
        HMACSHA1.HashData(key, counter, mac);

        var offset = mac[^1] & 0x0F;
        var truncated = BinaryPrimitives.ReadUInt32BigEndian(mac.Slice(offset, 4)) & 0x7FFFFFFF;

        return (truncated % DigitCeiling).ToString($"D{Digits}");
    }
}
