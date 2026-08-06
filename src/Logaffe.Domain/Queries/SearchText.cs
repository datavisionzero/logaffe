namespace Logaffe.Domain.Queries;

/// <summary>
/// The free-text narrowing, matched as a case-insensitive substring of an
/// entry's rendered message.
/// </summary>
/// <remarks>
/// A search text is at least <see cref="MinimumLength"/> characters, and a
/// shorter one is refused rather than run: the trigram index matches in
/// three-character pieces and cannot serve anything shorter, so a two-character
/// search scans the whole project — measured at 75 seconds over ten million
/// entries, which is a way to occupy the installation with one request
/// (ADR 0025). The rule binds the query surface, so the operator and the agent
/// meet the same one.
/// </remarks>
public sealed record SearchText
{
    public const int MinimumLength = 3;

    private SearchText(string value) => Value = value;

    public string Value { get; }

    public static SearchText Create(string? value) =>
        TryCreate(value, out var text)
            ? text
            : throw new ArgumentException(
                $"A search text is at least {MinimumLength} characters.", nameof(value));

    public static bool TryCreate(string? value, out SearchText text)
    {
        // The length that counts is the one that reaches the index, so it is
        // measured after trimming rather than before.
        var trimmed = value?.Trim();
        if (trimmed is null || trimmed.Length < MinimumLength)
        {
            text = null!;
            return false;
        }

        text = new SearchText(trimmed);
        return true;
    }

    public override string ToString() => Value;
}
