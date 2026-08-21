namespace Logaffe.Application.Ports;

/// <summary>
/// What the database occupies on the disk it sits on.
/// </summary>
/// <remarks>
/// <para>
/// One number, exact, and asked of the store itself rather than worked out from
/// the rows: it is what the operator's disk says, so it is every table, every
/// index and the space a sweep freed and left claimed
/// (<c>docs/storage.md</c>) — which is the honest answer to "what is this
/// costing me", and the arithmetic of
/// <see cref="Domain.Storage.Footprint"/> is not.
/// </para>
/// <para>
/// A port of its own rather than a third question on
/// <see cref="IDatabaseProbe"/>. That one is asked on the way up, to decide
/// whether the installation can serve at all, and a size has nothing to do with
/// readiness.
/// </para>
/// </remarks>
public interface IStoreFootprint
{
    /// <summary>
    /// Bytes the whole database holds, this moment. It costs one call and reads
    /// no table.
    /// </summary>
    Task<long> HeldBytesAsync(CancellationToken cancellationToken);
}
