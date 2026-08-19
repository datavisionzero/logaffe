namespace Logaffe.Collector;

/// <summary>
/// How often this reports.
/// </summary>
/// <remarks>
/// <para>
/// The same minute <c>docs/metrics.md</c> states and the installation's own
/// <c>Sampling.Interval</c> holds — <b>written twice on purpose</b>. A collector
/// references none of the four layers (<c>docs/codebase.md</c>), so it cannot
/// share the constant, and a package published to share one number would be a
/// dependency on every machine the operator owns for a value that has never
/// changed.
/// </para>
/// <para>
/// What keeps the two honest is that neither has to be right for the other to
/// work: the installation stamps a sample when it arrives and collapses two in
/// one minute to the first, so a collector reporting faster loses the extra and
/// one reporting slower leaves a gap. It is not a handshake, and there is
/// nothing here for a version to negotiate.
/// </para>
/// </remarks>
internal static class Sampling
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);
}
