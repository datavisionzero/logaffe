using Logaffe.Application.Ports;

namespace Logaffe.Application.Operations;

/// <summary>
/// Removes the entries that have outlived the window of the project they belong
/// to, and the entries of projects that no longer exist.
/// </summary>
/// <remarks>
/// <para>
/// It counts from <b>receipt time</b>, which is the only one of the two clocks a
/// sender cannot get wrong (ADR 0007): ordering by the sender's clock and
/// expiring by ours is what keeps an application with a wrong clock from either
/// keeping its rows forever or losing them on arrival.
/// </para>
/// <para>
/// <b>Rows, in bounded portions, rather than dropped partitions.</b> Retention
/// is configured per project, so a partition could only be dropped once
/// everything inside it had expired and a project keeping entries for seven days
/// would keep them for as long as the longest window in the installation, which
/// may be a year (ADR 0023, ADR 0048). Deleting rows is the cost of
/// that promise, and the portion is what keeps it from being one long
/// transaction across a table other projects are still being written to.
/// </para>
/// <para>
/// <b>It also takes what a deleted project left.</b> A project goes at once and
/// its entries follow in the background (ADR 0019), and this is that background:
/// the walk over the projects cannot reach them, because the row that named
/// their window is gone. They are removed whole rather than by a window, which
/// is the same thing said with a window of nothing.
/// </para>
/// <para>
/// A pass that throws is not this act's problem. <c>PeriodicService</c> logs it
/// and the next interval is the retry; what a failed pass leaves behind is rows
/// that live a little longer than they had to, which is what would have happened
/// anyway had the installation been down.
/// </para>
/// </remarks>
public sealed class SweepExpiredEntries(IProjects projects, IEntries entries, TimeProvider clock)
{
    /// <summary>
    /// How many rows one statement removes.
    /// </summary>
    /// <remarks>
    /// A product value rather than something the operator tunes, and chosen for
    /// the shape rather than measured: large enough that a day of a busy
    /// project is a handful of statements, small enough that each one is over
    /// in well under the five seconds a read is given (ADR 0026) and leaves gaps
    /// for the traffic and the autovacuum that ADR 0023 configures for this
    /// table.
    /// </remarks>
    public const int Portion = 10_000;

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var live = await projects.ListAsync(cancellationToken);

        foreach (var project in live)
        {
            await RemoveAsync(project.Id, now - project.Retention.Duration, cancellationToken);
        }

        var known = live.Select(project => project.Id).ToHashSet();
        var abandoned = await entries.ProjectsWithEntriesAsync(cancellationToken);

        foreach (var projectId in abandoned.Where(id => !known.Contains(id)))
        {
            // Everything, because there is no window left to read: the project
            // that held it was deleted, and nothing can reach these rows in the
            // meantime — every query runs inside a project.
            await RemoveAsync(projectId, DateTimeOffset.MaxValue, cancellationToken);
        }
    }

    private async Task RemoveAsync(
        Guid projectId, DateTimeOffset receivedBefore, CancellationToken cancellationToken)
    {
        int removed;
        do
        {
            removed = await entries.RemoveReceivedBeforeAsync(
                projectId, receivedBefore, Portion, cancellationToken);
        }
        while (removed == Portion);
    }
}
