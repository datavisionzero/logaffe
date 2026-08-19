namespace Logaffe.Collector;

/// <summary>
/// The filesystems the operator named, measured through the root the container
/// sees them under.
/// </summary>
/// <remarks>
/// <para>
/// <b>Which mounts are read is named in the configuration</b> and never
/// discovered (<c>docs/metrics.md</c>): a machine that mounts forty container
/// overlays does not silently become forty rows a minute. This class therefore
/// enumerates nothing — it measures a list somebody wrote.
/// </para>
/// <para>
/// A path is measured under <see cref="CollectorSettings.RootPath"/>, because
/// the container's own <c>/</c> is the image and not the machine. The mount is
/// reported under the name the operator gave — <c>/</c>, not <c>/rootfs</c> —
/// since what they asked about is a path on their machine.
/// </para>
/// </remarks>
internal sealed class MountedFilesystems(string rootPath, IReadOnlyList<string> mounts)
{
    /// <summary>
    /// Which mounts could not be measured last time, so that a disk going away
    /// and coming back is two lines rather than one a minute forever.
    /// </summary>
    private readonly HashSet<string> _missing = new(StringComparer.Ordinal);

    public IReadOnlyList<Filesystem> Read()
    {
        var read = new List<Filesystem>(mounts.Count);

        foreach (var mount in mounts)
        {
            if (TryMeasure(mount, out var filesystem))
            {
                if (_missing.Remove(mount))
                {
                    Say.Line($"{mount} can be measured again.");
                }

                read.Add(filesystem);
            }
            else if (_missing.Add(mount))
            {
                // Said once and then not again. A mount named in the
                // configuration and not present on the machine is an ordinary
                // mistake — a path typed with a trailing word, a disk not
                // mounted yet — and the rest of the reading is still worth
                // delivering, so this is a line and not an exit.
                Say.Line($"{mount} cannot be measured, so it is left out of the reading.");
            }
        }

        return read;
    }

    /// <summary>
    /// One filesystem, measured the way <c>df</c> measures it.
    /// </summary>
    /// <remarks>
    /// <b>Used is total less all free blocks</b>, which counts the blocks the
    /// kernel reserves for root as used — the same arithmetic <c>df</c> does, so
    /// that the number here and the number an operator gets from a shell on the
    /// same machine agree. <c>AvailableFreeSpace</c> is the other candidate and
    /// it excludes the reserve, which would report a full disk as 95% full.
    /// </remarks>
    private bool TryMeasure(string mount, out Filesystem filesystem)
    {
        filesystem = null!;

        try
        {
            var at = Under(rootPath, mount);

            if (!Directory.Exists(at))
            {
                return false;
            }

            var drive = new DriveInfo(at);
            var total = drive.TotalSize;

            // A pseudo-filesystem — a `tmpfs` of nothing, a mount that answered
            // but has no size — is not a disk anybody asked about, and a total
            // of zero would be a track drawn against no ceiling.
            if (total <= 0)
            {
                return false;
            }

            filesystem = new Filesystem(mount, Math.Max(total - drive.TotalFreeSpace, 0), total);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// A path on the machine, as the container reaches it. The root itself is
    /// the root of the mount and not a directory inside it.
    /// </summary>
    internal static string Under(string rootPath, string mount) =>
        mount == "/" ? rootPath : rootPath + mount.TrimEnd('/');
}
