using System.Buffers.Text;
using System.Security.Cryptography;

namespace Logaffe.Domain.Operators;

/// <summary>
/// The value a signed-in browser holds and presents, and the only thing that
/// makes a <see cref="Session"/> anybody's.
/// </summary>
/// <remarks>
/// <para>
/// It is stored as a single fast SHA-256, exactly as a backup code is, and for
/// the same reasons (ADR 0032): it is drawn by the installation at full entropy,
/// so there is no candidate list a slow hash would defend against, and it is not
/// recoverable — a session that has to be shown again is a session, not a thing
/// to read back. Unlike the operator's three secrets it is also not the
/// operator's to keep: losing it costs a sign-in.
/// </para>
/// <para>
/// Because the hash is deterministic and the value carries all of its entropy,
/// this needs no identifier naming its row the way a token does (ADR 0031). One
/// account holds a handful of sessions, so the presented value is compared
/// against them in constant time rather than looked up.
/// </para>
/// </remarks>
public sealed class SessionSecret
{
    /// <summary>
    /// Two hundred and fifty-six bits, drawn from a cryptographic source. It is
    /// never typed by a person — it lives in a cookie — so it is bytes rather
    /// than symbols out of the token alphabet.
    /// </summary>
    public const int LengthInBytes = 32;

    /// <summary>
    /// How long that is written down — forty-three characters, which is the one
    /// length a session secret has.
    /// </summary>
    public static readonly int TextLength = Base64Url.GetEncodedLength(LengthInBytes);

    private SessionSecret(string text, byte[] hash)
    {
        Text = text;
        Hash = hash;
    }

    /// <summary>
    /// The secret as it travels, in base64url so that it survives a cookie
    /// without escaping.
    /// </summary>
    public string Text { get; }

    /// <summary>What the row holds.</summary>
    public byte[] Hash { get; }

    /// <summary>Draws a fresh secret, which is what starting a session does.</summary>
    public static SessionSecret Mint()
    {
        var material = RandomNumberGenerator.GetBytes(LengthInBytes);
        var secret = new SessionSecret(
            Base64Url.EncodeToString(material), SHA256.HashData(material));

        CryptographicOperations.ZeroMemory(material);

        return secret;
    }

    /// <summary>
    /// Reads a presented secret. Anything that is not one of these — the wrong
    /// length, a character outside base64url — is refused here, before any
    /// session is fetched.
    /// </summary>
    public static bool TryParse(string? value, out SessionSecret secret)
    {
        secret = null!;

        // The shape first, and only then the decoding: what arrives here is
        // whatever a browser sent, and the decoder answers a value it cannot
        // read by throwing rather than by saying no.
        if (value is null || value.Length != TextLength || !Base64Url.IsValid(value))
        {
            return false;
        }

        Span<byte> material = stackalloc byte[LengthInBytes];
        if (!Base64Url.TryDecodeFromChars(value, material, out var decoded)
            || decoded != LengthInBytes)
        {
            return false;
        }

        // Re-encoded rather than kept as it arrived, so that one secret has one
        // text however it was written down.
        secret = new SessionSecret(
            Base64Url.EncodeToString(material), SHA256.HashData(material));

        CryptographicOperations.ZeroMemory(material);

        return true;
    }

    /// <summary>
    /// Redacted, so that a session secret reaching a log line or an exception
    /// message by way of an interpolation carries nothing at all.
    /// </summary>
    public override string ToString() => "…";
}
