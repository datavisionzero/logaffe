using System.Security.Cryptography;

namespace Logaffe.Domain.Tokens;

/// <summary>
/// The characters a token's identifier and secret are drawn from.
/// </summary>
/// <remarks>
/// Thirty-two symbols: the lowercase letters and the digits, less <c>l</c>,
/// <c>o</c>, <c>0</c> and <c>1</c>, which are the pairs a person transcribing a
/// token by hand confuses. The underscore is deliberately absent because it
/// separates a token's three parts, so no part can contain one and the split is
/// unambiguous.
/// </remarks>
public static class TokenAlphabet
{
    public const string Symbols = "abcdefghijkmnpqrstuvwxyz23456789";

    /// <summary>
    /// Draws <paramref name="length"/> symbols from a cryptographic source.
    /// </summary>
    public static string Random(int length) =>
        RandomNumberGenerator.GetString(Symbols, length);

    /// <summary>
    /// Whether every character of <paramref name="value"/> is one of
    /// <see cref="Symbols"/>. An empty value is covered vacuously; length is the
    /// caller's question.
    /// </summary>
    public static bool Covers(ReadOnlySpan<char> value)
    {
        foreach (var character in value)
        {
            if (!Symbols.Contains(character))
            {
                return false;
            }
        }

        return true;
    }
}
