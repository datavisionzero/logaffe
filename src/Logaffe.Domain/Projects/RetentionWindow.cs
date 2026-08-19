namespace Logaffe.Domain.Projects;

/// <summary>
/// The period entries or samples are kept for, counted from receipt time, after
/// which they are removed.
/// </summary>
/// <remarks>
/// <para>
/// A project owns one of these for its entries. Samples have one too, set once
/// for the installation rather than per host, because they belong to a host and
/// there is no reason to keep one machine's numbers longer than another's
/// (<c>docs/metrics.md</c>). It is one type because it is one rule: the same
/// floor, and the same ceiling no installation can raise.
/// </para>
/// Time is the only limit a project has — no size cap, no row quota, and no
/// interaction between limits. The number is the operator's up to a ceiling no
/// installation can raise (ADR 0020): without one, a settings box quietly turns
/// logaffe into the multi-year archive it says it is not, and the assumptions
/// the rest of the product rests on stop being true without anyone deciding
/// that they should.
/// </remarks>
public sealed record RetentionWindow
{
    public const int MinimumDays = 1;
    public const int MaximumDays = 90;

    private RetentionWindow(int days) => Days = days;

    public int Days { get; }

    public TimeSpan Duration => TimeSpan.FromDays(Days);

    public static RetentionWindow OfDays(int days) =>
        TryOfDays(days, out var window)
            ? window
            : throw new ArgumentOutOfRangeException(
                nameof(days),
                days,
                $"A retention window is between {MinimumDays} and {MaximumDays} days.");

    public static bool TryOfDays(int days, out RetentionWindow window)
    {
        if (days is < MinimumDays or > MaximumDays)
        {
            window = null!;
            return false;
        }

        window = new RetentionWindow(days);
        return true;
    }

    public override string ToString() => $"{Days}d";
}
