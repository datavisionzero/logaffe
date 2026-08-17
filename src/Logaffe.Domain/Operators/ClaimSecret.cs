using System.Security.Cryptography;
using System.Text;
using Logaffe.Domain.Tokens;

namespace Logaffe.Domain.Operators;

/// <summary>
/// The value that has to be presented to claim an installation, when that
/// installation is guarded by one.
/// </summary>
/// <remarks>
/// <para>
/// It guards the act of claiming and nothing else (ADR 0040): it is not a factor
/// beside the password, it admits nothing on its own, and it stops meaning
/// anything the moment the installation has an operator. An installation guarded
/// this way has no deadline, because a door that is locked does not need a clock.
/// </para>
/// <para>
/// Either the installation draws one on its first start or the operator sets one
/// as configuration. A drawn secret is stored as a single fast SHA-256, exactly
/// as a session secret and a backup code are and for the same reason
/// (ADR 0032): it carries all of its own entropy, so there is no candidate list
/// a slow hash would defend against. A supplied one is stored nowhere at all —
/// it is compared against what configuration says.
/// </para>
/// <para>
/// A class rather than a record, like <see cref="Password"/> and
/// <see cref="TokenText"/>: this is proved by <see cref="Matches"/> in constant
/// time and never by equality.
/// </para>
/// </remarks>
public sealed class ClaimSecret
{
    /// <summary>
    /// Thirty-two symbols of <see cref="TokenAlphabet"/> — one hundred and sixty
    /// bits, in the alphabet that leaves out the pairs a person transcribing
    /// confuses. A drawn claim secret is read off a terminal and typed into a
    /// browser on another machine more often than a token ever is.
    /// </summary>
    public const int DrawnLength = 32;

    /// <summary>
    /// What a supplied one has to clear. This is the one public door a guess
    /// opens, so a short value is refused at the start rather than quietly
    /// accepted — and unlike a password this is pasted rather than recited, so
    /// anything below this is somebody typing a word.
    /// </summary>
    public const int MinimumLength = 16;

    /// <summary>
    /// A bound on what is hashed on a public, pre-authentication path, in the
    /// same spirit as <see cref="Password.MaximumLength"/>. Nothing about a
    /// claim secret wants to be longer than this.
    /// </summary>
    public const int MaximumLength = 256;

    private ClaimSecret(string text, byte[] hash)
    {
        Text = text;
        Hash = hash;
    }

    /// <summary>
    /// The secret as it is handed over and as it comes back. It is held for the
    /// length of one request, or for as long as it takes to write the file the
    /// operator reads it out of.
    /// </summary>
    public string Text { get; }

    /// <summary>What the row holds, when the installation drew this one.</summary>
    public byte[] Hash { get; }

    /// <summary>
    /// Draws a fresh secret, which is what a first start with no supplied one
    /// does — and what Host Recovery does, because that is the moment the
    /// installation's notion of who may claim it changes (ADR 0013).
    /// </summary>
    public static ClaimSecret Draw() => Of(TokenAlphabet.Random(DrawnLength));

    /// <summary>
    /// Reads a secret that has to be a well-formed one: the value out of
    /// configuration, which the installation refuses to start without.
    /// </summary>
    public static bool TryCreate(string? value, out ClaimSecret secret)
    {
        if (value is null || value.Length < MinimumLength || value.Length > MaximumLength)
        {
            secret = null!;
            return false;
        }

        secret = Of(value);
        return true;
    }

    /// <summary>
    /// Reads a presented secret, which is whatever somebody typed into the claim
    /// screen.
    /// </summary>
    /// <remarks>
    /// Deliberately looser than <see cref="TryCreate"/>: a presented value that
    /// is too short is not a malformed request, it is a wrong secret, and the
    /// only shape that matters here is the bound on what gets hashed. What is
    /// refused is the empty value, which is the request that presented nothing.
    /// </remarks>
    public static bool TryRead(string? value, out ClaimSecret secret)
    {
        if (string.IsNullOrEmpty(value) || value.Length > MaximumLength)
        {
            secret = null!;
            return false;
        }

        secret = Of(value);
        return true;
    }

    /// <summary>Whether this is the secret that hash was written from.</summary>
    public bool Matches(byte[]? hash) =>
        hash is not null && CryptographicOperations.FixedTimeEquals(Hash, hash);

    /// <summary>
    /// Whether this is the same secret as that one, which is how a presented
    /// value is compared against the one configuration supplied — through the
    /// hashes, so that the comparison takes the same time whatever was typed.
    /// </summary>
    public bool Matches(ClaimSecret? other) => Matches(other?.Hash);

    /// <summary>
    /// Redacted, so that a claim secret reaching a log line or an exception
    /// message by way of an interpolation carries nothing at all. The one place
    /// it is written out in full says so in as many words.
    /// </summary>
    public override string ToString() => "…";

    private static ClaimSecret Of(string value) =>
        new(value, SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
