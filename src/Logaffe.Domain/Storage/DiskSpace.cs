namespace Logaffe.Domain.Storage;

/// <summary>
/// How full the filesystem holding the database is, as the newest reading of it
/// says.
/// </summary>
/// <remarks>
/// Both numbers rather than a share, for the reason the reading itself carries
/// both (<see cref="Hosts.FilesystemReading"/>): how full a disk is and how
/// large it is answer different questions, and only one of them survives the
/// division. The operator deciding on a window needs the first to know whether
/// the window fits and the second to know what fitting would mean.
/// </remarks>
/// <param name="Free">Bytes the filesystem has left.</param>
/// <param name="Total">Bytes it holds.</param>
public sealed record DiskSpace(long Free, long Total);
