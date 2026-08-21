using Logaffe.Application.Ports;
using Logaffe.Domain.Hosts;
using Logaffe.Domain.Projects;
using Logaffe.Domain.Storage;

namespace Logaffe.Application.Operations;

/// <summary>
/// What a retention window costs, read while the operator is choosing one.
/// </summary>
/// <remarks>
/// <para>
/// This is what raising the ceiling to a year rests on (ADR 0048): the number no
/// longer does the work of keeping the product bounded, so the field states the
/// cost instead. Three numbers, one of which moves with what is being typed —
/// what the installation holds today, what this window implies, and what the
/// disk has left.
/// </para>
/// <para>
/// <b>It refuses nothing and is compared with nothing.</b> No quota, no size cap
/// and no drop-oldest: the operator sees the arithmetic and decides, and time
/// stays the only limit a project has. It sits beside
/// <see cref="CountEntriesOutsideWindow"/> — that one says what a lowering
/// destroys, this one says what a window costs — and like it, it is a read in
/// front of the act rather than part of it.
/// </para>
/// <para>
/// <b>Both windows are asked the same three questions</b>, and only the middle
/// one is answered differently: a project's is the tally of ADR 0047 turned into
/// bytes, and the installation's sample window is the shape of what its
/// collectors report. The other two are the installation's and are the same
/// numbers on both screens, which is the point of showing them — the operator is
/// deciding about one disk.
/// </para>
/// <para>
/// <b>It reads no entry and no sample.</b> Everything here is one row of the
/// installation, a handful of tally rows, the newest report of each host and one
/// call to the store for its own size — nothing that grows with the entries, so
/// nothing that gets slower as the thing it describes gets larger.
/// </para>
/// </remarks>
public sealed class ReadTheFootprint(
    IProjects projects,
    IHosts hosts,
    ITallies tallies,
    ISampleReader samples,
    IInstallation installation,
    IStoreFootprint store,
    TimeProvider clock)
{
    /// <summary>
    /// What <paramref name="proposed"/> would cost this project, or <c>null</c>
    /// when there is no such project — which is what one deleted in another tab
    /// looks like.
    /// </summary>
    public async Task<Footprint?> OfProjectAsync(
        Guid id, RetentionWindow proposed, CancellationToken cancellationToken)
    {
        var project = await projects.FindAsync(id, cancellationToken);
        if (project is null)
        {
            return null;
        }

        return new Footprint(
            await store.HeldBytesAsync(cancellationToken),
            await ImpliedByEntriesAsync(project.Id, proposed, cancellationToken),
            await DiskAsync(cancellationToken));
    }

    /// <summary>
    /// What <paramref name="proposed"/> would cost the installation in samples,
    /// which is one window for every host there is.
    /// </summary>
    public async Task<Footprint> OfSamplesAsync(
        RetentionWindow proposed, CancellationToken cancellationToken)
    {
        var held = await store.HeldBytesAsync(cancellationToken);

        var named = await installation.ReadHostAsync(cancellationToken);
        var reports = await samples.NewestReportsAsync(
            [.. (await hosts.ListAsync(cancellationToken)).Select(host => host.Id)],
            cancellationToken);

        // A host that has never reported is not in the reports, and it is right
        // that it is not: what a machine writes a minute is the shape of what it
        // last said, and one that has said nothing has no shape yet. An
        // installation where none of them has says nothing at all rather than
        // saying nought.
        long? implied = reports.Count == 0
            ? null
            : Footprint.OfSamples(
                reports.Count, reports.Sum(report => report.Filesystems.Count), proposed);

        return new Footprint(held, implied, Disk(reports, named));
    }

    /// <summary>
    /// The project's own rate over the fortnight behind it, as bytes at
    /// <paramref name="proposed"/> — or <c>null</c> when there is no fortnight
    /// behind it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The hour in progress is left out, because it is a fraction of an hour
    /// that would be divided as a whole one — so what is summed is exactly the
    /// fourteen days of closed hours the average is taken over. Hours nothing
    /// arrived in are the absent rows they are: a project that was quiet for a
    /// week is a project with a low rate, not a project with a short history.
    /// </para>
    /// <para>
    /// <b>A project younger than the fortnight is told so rather than
    /// extrapolated.</b> Two days multiplied up by a year is not a footprint,
    /// and the first fortnight of a project is exactly when somebody is setting
    /// its window (<see cref="Tallying.Baseline"/>).
    /// </para>
    /// </remarks>
    private async Task<long?> ImpliedByEntriesAsync(
        Guid projectId, RetentionWindow proposed, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();

        var oldest = await tallies.OldestHourAsync(projectId, cancellationToken);
        if (oldest is null || now - oldest.Value < Tallying.Baseline)
        {
            return null;
        }

        var until = Tallying.HourOf(now);
        var since = until - Tallying.Baseline;

        var counted = await tallies.ReadAsync(projectId, since, until, cancellationToken);

        return Footprint.OfEntries(
            counted.Sum(hour => hour.Entries), until - since, proposed);
    }

    private async Task<DiskSpace?> DiskAsync(CancellationToken cancellationToken)
    {
        var named = await installation.ReadHostAsync(cancellationToken);
        if (named is null)
        {
            return null;
        }

        var reports = await samples.NewestReportsAsync([named.HostId], cancellationToken);

        return Disk(reports, named);
    }

    /// <summary>
    /// What the named mount last said about itself, or <c>null</c> when this
    /// installation names no host, when that host is not reporting, or when the
    /// mount it names is not among what arrives.
    /// </summary>
    /// <remarks>
    /// The three absences are one answer on purpose. What the screen has to say
    /// is that there is no disk reading here, and which of the three it is is a
    /// thing the operator settles where the host is named rather than on a field
    /// about retention.
    /// </remarks>
    private static DiskSpace? Disk(
        IReadOnlyList<NewestReport> reports, InstallationHost? named)
    {
        if (named is null)
        {
            return null;
        }

        var reading = reports
            .Where(report => report.HostId == named.HostId)
            .SelectMany(report => report.Filesystems)
            .FirstOrDefault(filesystem => filesystem.MountPath == named.Mount);

        return reading is null
            ? null
            // A filesystem keeps blocks back for the superuser, so what is used
            // and what is free do not add up to what it holds. The reading
            // carries the two the collector read, and this is the subtraction it
            // implies — floored, because a disk that is fuller than it is large
            // has nothing left rather than a negative amount of it.
            : new DiskSpace(Math.Max(0, reading.Total - reading.Used), reading.Total);
    }
}
