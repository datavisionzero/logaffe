using System.Security.Cryptography;
using System.Text;
using Logaffe.Domain.Tokens;

namespace Logaffe.Domain.Operators;

/// <summary>
/// A backup code as it is shown once and as it is typed back.
/// </summary>
/// <remarks>
/// <para>
/// It is drawn by the installation rather than chosen, which is what makes a
/// fast hash with no salt the right storage for it: there is no candidate list
/// to grind, and a salt defends against a precomputed table that cannot exist
/// for a value of this size (ADR 0032). What it needs is a constant-time
/// comparison, and that is <see cref="BackupCode.Matches"/>.
/// </para>
/// <para>
/// It is written in the token alphabet, and for the same reason that alphabet
/// exists: this is a value a person reads off a piece of paper and types with
/// one finger, so the characters they would confuse are not in it
/// (<see cref="TokenAlphabet"/>). The groups are a display convenience —
/// <see cref="TryParse"/> ignores them, along with spaces and capitals, because
/// refusing a code over a dash is refusing the operator their way back in.
/// </para>
/// <para>
/// A class rather than a record, as everywhere else a secret is: it is compared
/// by <see cref="BackupCode.Matches"/> and never by equality.
/// </para>
/// </remarks>
public sealed class BackupCodeText
{
    /// <summary>
    /// Sixteen symbols — eighty bits. It is sized against an attacker holding a
    /// stolen dump and a fast hash rather than against somebody guessing over
    /// the network, which is what ADR 0032 means by full entropy: there has to
    /// be nothing worth grinding.
    /// </summary>
    public const int Length = 16;

    private const int GroupLength = 4;
    private const char GroupSeparator = '-';

    private BackupCodeText(string symbols)
    {
        Symbols = symbols;
        Hash = SHA256.HashData(Encoding.UTF8.GetBytes(symbols));
    }

    /// <summary>The code itself, ungrouped, which is what is hashed.</summary>
    public string Symbols { get; }

    /// <summary>
    /// A single fast SHA-256, no salt, which is what a row holds. It is never
    /// recoverable: unlike a token, a backup code is precisely what stands in
    /// when the operator can reach nothing, so there is no session that could be
    /// trusted to display one (ADR 0032).
    /// </summary>
    public byte[] Hash { get; }

    /// <summary>
    /// Grouped for the sheet of paper the operator prints, and the only form
    /// they are ever shown.
    /// </summary>
    public string Display => string.Join(
        GroupSeparator,
        Enumerable.Range(0, Length / GroupLength)
            .Select(group => Symbols.Substring(group * GroupLength, GroupLength)));

    /// <summary>Draws a fresh code.</summary>
    public static BackupCodeText Mint() => new(TokenAlphabet.Random(Length));

    /// <summary>
    /// Reads a typed code, forgiving everything about how it was typed and
    /// nothing about what it is. A value that is not one of these is refused
    /// here, before the operator's codes are fetched and without the database
    /// being asked anything at all.
    /// </summary>
    public static bool TryParse(string? value, out BackupCodeText code)
    {
        code = null!;
        if (value is null)
        {
            return false;
        }

        var symbols = new string([
            .. value.Where(character => !char.IsWhiteSpace(character) && character != GroupSeparator)
                .Select(char.ToLowerInvariant),
        ]);

        if (symbols.Length != Length || !TokenAlphabet.Covers(symbols))
        {
            return false;
        }

        code = new BackupCodeText(symbols);
        return true;
    }

    /// <summary>
    /// Redacted, so that a code reaching a log line or an exception message by
    /// way of an interpolation carries nothing at all. What is shown to the
    /// operator is asked for by name, through <see cref="Display"/>.
    /// </summary>
    public override string ToString() => "…";
}
