namespace Logaffe.Domain.Hosts;

/// <summary>
/// The last thing one host said: when it reported, and the filesystems it
/// reported on.
/// </summary>
/// <remarks>
/// <para>
/// It is the newest sample rather than a range, and it answers two different
/// questions with one read — how full the disk under this installation is right
/// now, and how many rows a minute the machines between them write, which is
/// what a sample retention window costs (<see cref="Storage.Footprint"/>).
/// </para>
/// <para>
/// <b>The filesystems are the ones the host reports, not the ones it has.</b>
/// They are named in its collector's configuration, so this list is what that
/// machine was asked about — and a host reporting none of them is an ordinary
/// host, not a broken one.
/// </para>
/// </remarks>
public sealed record NewestReport(
    Guid HostId,
    DateTimeOffset ReceiptTime,
    IReadOnlyList<FilesystemReading> Filesystems);
