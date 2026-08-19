using Logaffe.Domain.Hosts;

namespace Logaffe.Application.Ports;

/// <summary>
/// The samples an installation holds: what a delivery writes, and what the sweep
/// takes out again.
/// </summary>
/// <remarks>
/// <para>
/// Reading them is <see cref="ISampleReader"/>, a port of its own for the reason
/// <see cref="IEntryReader"/> is one — it is a different surface with different
/// consumers, and nothing on this one hands a sample back.
/// </para>
/// <para>
/// Unlike <see cref="IEntries"/>, this side goes through EF Core rather than
/// around it. The log path earns a binary <c>COPY</c> at eleven thousand entries
/// a second (ADR 0003); a handful of hosts writing a few rows a minute earns
/// nothing of the sort, and ADR 0003's rule read as written puts samples on the
/// ordinary side of it.
/// </para>
/// </remarks>
public interface ISamples
{
    /// <summary>
    /// Writes one reading and the filesystem readings taken with it, together.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One call because it is one reading: a sample whose filesystems landed
    /// without it, or the other way round, is the half-sample the endpoint
    /// refuses a malformed delivery to avoid.
    /// </para>
    /// <para>
    /// <b>A host reporting twice for the same moment writes once.</b> The key is
    /// natural — the host and the clock — so the second delivery is a conflict
    /// the write resolves by keeping what is there, rather than a second row
    /// that quietly doubles a machine on the band.
    /// </para>
    /// </remarks>
    Task WriteAsync(
        Sample sample,
        IReadOnlyList<FilesystemReading> filesystems,
        CancellationToken cancellationToken);

    /// <summary>
    /// Every host identity the sample tables still hold rows for.
    /// </summary>
    /// <remarks>
    /// Asked because deleting a host leaves its samples behind: the sweep walks
    /// the hosts, and a host that no longer exists is not on that walk. This is
    /// the other end of it — what the tables say is in there, against what the
    /// installation says exists.
    /// </remarks>
    Task<IReadOnlyList<Guid>> HostsWithSamplesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Removes up to <paramref name="portion"/> of one host's samples that
    /// arrived before <paramref name="receivedBefore"/>, and answers how many
    /// went. The filesystem readings taken with them go too.
    /// </summary>
    Task<int> RemoveReceivedBeforeAsync(
        Guid hostId,
        DateTimeOffset receivedBefore,
        int portion,
        CancellationToken cancellationToken);

    /// <summary>
    /// How many samples across every host arrived before
    /// <paramref name="receivedBefore"/>.
    /// </summary>
    /// <remarks>
    /// What the operator is told before a lower window takes effect. It spans
    /// the installation rather than one host because the window does: there is
    /// one of them, and the question it answers is how much lowering it costs.
    /// </remarks>
    Task<long> CountReceivedBeforeAsync(
        DateTimeOffset receivedBefore, CancellationToken cancellationToken);
}
