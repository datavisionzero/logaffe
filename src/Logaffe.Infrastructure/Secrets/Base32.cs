namespace Logaffe.Infrastructure.Secrets;

/// <summary>
/// RFC 4648 base32, without padding.
/// </summary>
/// <remarks>
/// It is here for one reason: it is what every authenticator app reads a TOTP
/// secret in, both from a QR code and from the line of text underneath it for
/// the operator typing it by hand (ADR 0016). The base class libraries carry
/// base64 and base64url and not this, so it is thirty lines rather than a
/// package.
/// </remarks>
internal static class Base32
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
    private const int BitsPerSymbol = 5;
    private const int BitsPerByte = 8;

    public static string Encode(ReadOnlySpan<byte> value)
    {
        var encoded = new System.Text.StringBuilder((value.Length * BitsPerByte / BitsPerSymbol) + 1);

        var buffer = 0;
        var bits = 0;

        foreach (var octet in value)
        {
            buffer = (buffer << BitsPerByte) | octet;
            bits += BitsPerByte;

            while (bits >= BitsPerSymbol)
            {
                bits -= BitsPerSymbol;
                encoded.Append(Alphabet[(buffer >> bits) & 0b11111]);
            }
        }

        // The leftover bits are a symbol of their own, padded with zeroes on the
        // right. Nothing is appended to round the length up: an `otpauth:` URI
        // carries the secret unpadded, and the apps that read it expect that.
        if (bits > 0)
        {
            encoded.Append(Alphabet[(buffer << (BitsPerSymbol - bits)) & 0b11111]);
        }

        return encoded.ToString();
    }

    /// <summary>
    /// Reads what <see cref="Encode"/> wrote, forgiving lowercase, spaces and
    /// trailing padding — all three of which are ways the same secret is written
    /// down.
    /// </summary>
    public static bool TryDecode(string? value, out byte[] decoded)
    {
        decoded = [];
        if (value is null)
        {
            return false;
        }

        var octets = new List<byte>(value.Length * BitsPerSymbol / BitsPerByte);
        var buffer = 0;
        var bits = 0;

        foreach (var character in value)
        {
            if (character is '=' || char.IsWhiteSpace(character))
            {
                continue;
            }

            var symbol = Alphabet.IndexOf(char.ToUpperInvariant(character));
            if (symbol < 0)
            {
                return false;
            }

            buffer = (buffer << BitsPerSymbol) | symbol;
            bits += BitsPerSymbol;

            if (bits >= BitsPerByte)
            {
                bits -= BitsPerByte;
                octets.Add((byte)((buffer >> bits) & 0xFF));
            }
        }

        decoded = [.. octets];
        return true;
    }
}
