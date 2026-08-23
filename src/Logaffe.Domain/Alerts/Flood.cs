namespace Logaffe.Domain.Alerts;

/// <summary>
/// The arithmetic behind a project delivering far more than it does: what counts
/// as far more, against what that hour of the day normally holds for it
/// (<see cref="Baseline"/>).
/// </summary>
public static class Flood
{
    /// <summary>How many times its own hour a closed hour has to be.</summary>
    public const int Multiple = 10;

    /// <summary>
    /// The floor under the ratio, in entries, whatever the ratio says.
    /// </summary>
    /// <remarks>
    /// <b>It is absolute and it is not a ratio.</b> Two entries becoming twenty
    /// is a tenfold rise and is not an incident, in any project, ever. It is
    /// also what makes a baseline of nought safe: ten times nothing is nothing,
    /// so without this every first entry of a quiet hour would fire.
    /// </remarks>
    public const long Floor = 1000;

    /// <summary>
    /// Whether a closed hour of <paramref name="entries"/> is far enough above
    /// <paramref name="baseline"/> to say something about.
    /// </summary>
    public static bool Fires(long entries, long baseline) =>
        entries >= Floor && entries > baseline * Multiple;
}
