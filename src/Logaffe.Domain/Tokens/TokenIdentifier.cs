namespace Logaffe.Domain.Tokens;

/// <summary>
/// The non-secret part a token carries between its prefix and its secret,
/// naming the row that holds it.
/// </summary>
/// <remarks>
/// It exists because a token is stored encrypted rather than hashed (ADR 0022),
/// so a randomized ciphertext cannot be looked up by the value presented: the
/// identifier is what turns authentication into one indexed lookup instead of a
/// walk over every token an installation holds (ADR 0031). It admits nothing on
/// its own — the secret is the part after it — which is why it can be indexed
/// and compared with an ordinary equality.
/// </remarks>
public sealed record TokenIdentifier
{
    /// <summary>
    /// Twelve characters of <see cref="TokenAlphabet.Symbols"/> — sixty bits,
    /// which is a great deal more than it takes to name one of a few dozen rows
    /// without collisions. It is not sized as a secret and must not be read as
    /// one.
    /// </summary>
    public const int Length = 12;

    private TokenIdentifier(string value) => Value = value;

    public string Value { get; }

    /// <summary>Draws a fresh identifier.</summary>
    public static TokenIdentifier Mint() => new(TokenAlphabet.Random(Length));

    public static TokenIdentifier Create(string? value) =>
        TryCreate(value, out var identifier)
            ? identifier
            : throw new ArgumentException(
                $"A token identifier is {Length} characters of the token alphabet.",
                nameof(value));

    public static bool TryCreate(string? value, out TokenIdentifier identifier)
    {
        if (value is not { Length: Length } || !TokenAlphabet.Covers(value))
        {
            identifier = null!;
            return false;
        }

        identifier = new TokenIdentifier(value);
        return true;
    }

    public override string ToString() => Value;
}
