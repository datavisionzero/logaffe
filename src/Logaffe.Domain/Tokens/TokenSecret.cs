namespace Logaffe.Domain.Tokens;

/// <summary>
/// The guard both token kinds share: a token row without its ciphertext is a
/// credential nothing can ever match, which is a corrupt row rather than a
/// revoked one.
/// </summary>
internal static class TokenSecret
{
    public static byte[] Encrypted(byte[]? value, string parameterName) =>
        value is { Length: > 0 }
            ? value
            : throw new ArgumentException(
                "A token holds its secret encrypted.", parameterName);
}
