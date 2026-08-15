using Logaffe.Application.Ports;

namespace Logaffe.Infrastructure.Secrets;

/// <summary>
/// The host volume as a set of files, which is what a backup has to put into an
/// artifact and a restore has to put back.
/// </summary>
/// <remarks>
/// It sits beside <see cref="HostVolumeKey"/> because it is the same directory:
/// the key is the file that makes an artifact worth having, and this reports
/// what is there beside it without judging any of it. Which of those files an
/// artifact carries is decided one layer up, by
/// <c>TakeABackup</c> — the volume knows what it holds, not what a backup is
/// for.
/// </remarks>
public sealed class HostVolume(string volumePath) : IHostVolume
{
    public string Path => volumePath;

    public IReadOnlyList<string> Files()
    {
        if (!Directory.Exists(volumePath))
        {
            return [];
        }

        return
        [
            .. Directory
                .EnumerateFiles(volumePath, "*", SearchOption.AllDirectories)
                .Select(Relative)
                .Order(StringComparer.Ordinal),
        ];
    }

    public Stream OpenRead(string relativePath) =>
        new FileStream(
            System.IO.Path.Combine(volumePath, relativePath),
            new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                // The file log on this volume is held open and appended to by the
                // installation this is running beside (ADR 0002), so reading it
                // has to be something two processes can do at once.
                Share = FileShare.ReadWrite | FileShare.Delete,
            });

    public Stream Create(string relativePath)
    {
        var path = System.IO.Path.Combine(volumePath, relativePath);

        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);

        var options = new FileStreamOptions
        {
            Mode = FileMode.Create,
            Access = FileAccess.Write,
        };

        // The key comes back readable by its owner and nobody else, set as the
        // file is created rather than afterwards, so there is no moment at which
        // it is not — the same rule HostVolumeKey follows when it writes one.
        if (!OperatingSystem.IsWindows() && relativePath.StartsWith("keys/", StringComparison.Ordinal))
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        return new FileStream(path, options);
    }

    /// <summary>
    /// Forward slashes whatever the host writes them as: the artifact is a tar,
    /// and a tar's paths are not the local filesystem's.
    /// </summary>
    private string Relative(string path) =>
        System.IO.Path.GetRelativePath(volumePath, path)
            .Replace(System.IO.Path.DirectorySeparatorChar, '/');
}
