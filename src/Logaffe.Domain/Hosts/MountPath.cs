namespace Logaffe.Domain.Hosts;

/// <summary>
/// The path of a filesystem a collector was told to measure.
/// </summary>
/// <remarks>
/// This is the only string in the whole sample shape, and the reason a sample
/// can be handed to an agent without the care an entry needs (ADR 0045): it is
/// not text that reached the machine from outside, it is what the operator wrote
/// into their own collector's configuration. The validation here is what keeps
/// that true — a value that is not a mount path is refused rather than stored,
/// so the column cannot quietly become somewhere to put arbitrary text.
/// </remarks>
public sealed record MountPath
{
    /// <summary>
    /// Long enough for any mount an operator would name and short enough that
    /// the key it sits in stays small. Nothing about a real mount point comes
    /// close to it.
    /// </summary>
    public const int MaxLength = 200;

    private MountPath(string value) => Value = value;

    public string Value { get; }

    public static MountPath Create(string? value) =>
        TryCreate(value, out var path)
            ? path
            : throw new ArgumentException(
                $"A mount path is an absolute path of at most {MaxLength} characters.",
                nameof(value));

    /// <summary>
    /// Whether <paramref name="value"/> is a mount path: absolute, no longer
    /// than <see cref="MaxLength"/>, and carrying nothing a path does not — no
    /// control characters, and no interior null.
    /// </summary>
    public static bool TryCreate(string? value, out MountPath path)
    {
        path = null!;

        var trimmed = value?.Trim();
        if (trimmed is not { Length: > 0 and <= MaxLength } || trimmed[0] != '/')
        {
            return false;
        }

        foreach (var character in trimmed)
        {
            if (char.IsControl(character))
            {
                return false;
            }
        }

        path = new MountPath(trimmed);
        return true;
    }

    public override string ToString() => Value;
}
