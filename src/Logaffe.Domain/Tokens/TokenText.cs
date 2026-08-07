using System.Security.Cryptography;
using System.Text;

namespace Logaffe.Domain.Tokens;

/// <summary>
/// A token as it travels and as it is read back: a prefix, an identifier and a
/// secret, separated by underscores.
/// </summary>
/// <remarks>
/// <para>
/// The prefix says which kind it is, so the wrong one is refused at the door
/// rather than three layers in (ADR 0021), and it is what makes a token that
/// leaked into a repository or a log entry something a scanner can find. The
/// identifier names the row. The secret is the only part that admits anything,
/// and it carries the entropy on its own (ADR 0031).
/// </para>
/// <para>
/// This is a class rather than a record on purpose: a token is verified by
/// <see cref="SecretMatches"/> and never by equality, and a type with an
/// <c>==</c> that looks like it would do would be an invitation to compare
/// secrets in variable time.
/// </para>
/// </remarks>
public sealed class TokenText
{
    public const string IngestPrefix = "logaffe_ingest";
    public const string AgentPrefix = "logaffe_agent";

    /// <summary>
    /// Fifty-two symbols of <see cref="TokenAlphabet"/> — two hundred and sixty
    /// bits. The identifier is added to the token rather than carved out of it,
    /// because splitting a credential into a public and a secret part is only
    /// sound if the secret part would have been enough alone (ADR 0031).
    /// </summary>
    public const int SecretLength = 52;

    private const char Separator = '_';

    private TokenText(TokenKind kind, TokenIdentifier identifier, string secret)
    {
        Kind = kind;
        Identifier = identifier;
        Secret = secret;
    }

    public TokenKind Kind { get; }

    public TokenIdentifier Identifier { get; }

    /// <summary>
    /// The part that admits a delivery. It is held in the clear only while it is
    /// being issued, verified, or read back for the operator — what the database
    /// holds is this value encrypted (ADR 0022).
    /// </summary>
    public string Secret { get; }

    /// <summary>The whole token, which is what a sender puts in a header.</summary>
    public string Text => string.Join(Separator, PrefixOf(Kind), Identifier.Value, Secret);

    /// <summary>Draws a fresh token of <paramref name="kind"/>.</summary>
    public static TokenText Mint(TokenKind kind) =>
        new(kind, TokenIdentifier.Mint(), TokenAlphabet.Random(SecretLength));

    /// <summary>
    /// Reassembles a token from the identifier stored beside it and the secret
    /// that was decrypted back, which is how the operator reads one back.
    /// </summary>
    public static TokenText From(TokenKind kind, TokenIdentifier identifier, string secret) =>
        IsWellFormedSecret(secret)
            ? new TokenText(kind, identifier, secret)
            : throw new ArgumentException(
                $"A token secret is {SecretLength} characters of the token alphabet.",
                nameof(secret));

    /// <summary>
    /// Reads a presented token. A value that is not one of these — the wrong
    /// prefix, the wrong shape, a character outside the alphabet — is refused
    /// here, before anything is looked up and without the database being asked
    /// anything at all.
    /// </summary>
    public static bool TryParse(string? value, out TokenText token)
    {
        token = null!;
        if (value is null)
        {
            return false;
        }

        // Four parts, because both prefixes carry one separator of their own.
        var parts = value.Split(Separator);
        if (parts.Length != 4)
        {
            return false;
        }

        var prefix = string.Join(Separator, parts[0], parts[1]);
        if (!TryParsePrefix(prefix, out var kind)
            || !TokenIdentifier.TryCreate(parts[2], out var identifier)
            || !IsWellFormedSecret(parts[3]))
        {
            return false;
        }

        token = new TokenText(kind, identifier, parts[3]);
        return true;
    }

    /// <summary>
    /// Whether this token's secret is the one that was stored, compared in
    /// constant time. `docs/ingestion.md` requires a bad token to reveal
    /// nothing, and a comparison that returns at the first differing character
    /// reveals how much of it was right.
    /// </summary>
    public bool SecretMatches(string storedSecret) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(Secret),
            Encoding.UTF8.GetBytes(storedSecret));

    /// <summary>
    /// The prefix without the token: identifying enough to say which endpoint a
    /// value belongs at, and never enough to use.
    /// </summary>
    public static string PrefixOf(TokenKind kind) => kind switch
    {
        TokenKind.Ingest => IngestPrefix,
        TokenKind.Agent => AgentPrefix,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown token kind."),
    };

    public static bool TryParsePrefix(string? prefix, out TokenKind kind)
    {
        switch (prefix)
        {
            case IngestPrefix:
                kind = TokenKind.Ingest;
                return true;
            case AgentPrefix:
                kind = TokenKind.Agent;
                return true;
            default:
                kind = default;
                return false;
        }
    }

    /// <summary>
    /// Redacted, so that a token reaching a log line or an exception message by
    /// way of an interpolation carries the part that identifies it and not the
    /// part that admits anything. The whole token is asked for by name, through
    /// <see cref="Text"/>.
    /// </summary>
    public override string ToString() =>
        $"{PrefixOf(Kind)}{Separator}{Identifier.Value}{Separator}…";

    private static bool IsWellFormedSecret(string? secret) =>
        secret is { Length: SecretLength } && TokenAlphabet.Covers(secret);
}
