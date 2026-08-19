using Serilog;
using Serilog.Configuration;

namespace Logaffe.Api.Hosting;

/// <summary>
/// logaffe's own log, which is a file on the host volume and never an entry in
/// the installation it belongs to (ADR 0002).
/// </summary>
/// <remarks>
/// It is written down once and used twice, because the command line writes to
/// the same file as the server: every use of Host Recovery goes here, and that
/// is the one place a record of it survives the reset it performs. Two
/// configurations would be two answers to where that record lives.
/// </remarks>
public static class FileLog
{
    /// <summary>
    /// The directory on the volume it is written into, which is also the one a
    /// backup leaves behind.
    /// </summary>
    private const string Directory = "logs";

    /// <summary>
    /// Bounded on purpose — it rolls by size and keeps a fixed number of files.
    /// An unbounded log on the same volume as the secrets is the most
    /// embarrassing possible way for a logging product to take its own
    /// installation down.
    /// </summary>
    public static LoggerConfiguration WriteToLogaffeFile(
        this LoggerSinkConfiguration sink, string volumePath) =>
        sink.File(
            Path.Combine(volumePath, Directory, "logaffe-.log"),
            rollingInterval: RollingInterval.Day,
            rollOnFileSizeLimit: true,
            fileSizeLimitBytes: 32L * 1024 * 1024,
            retainedFileCountLimit: 14,
            shared: true);

    /// <summary>
    /// Why this log cannot be written, or <see langword="null"/> when it can.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Asked by writing, because nothing else answers it: a directory that
    /// exists says nothing about a volume mounted read-only or a disk with
    /// nothing left, and both of those end with ADR 0002's record going nowhere.
    /// The file the check writes is removed again, and a volume that refuses the
    /// removal has already answered the question.
    /// </para>
    /// <para>
    /// It is a question worth asking at every start rather than at the first
    /// one. <see cref="KeyFitsService"/> makes the key exist so that a volume
    /// which cannot be written fails the start — and that holds the once, while
    /// the key is being created. Every start after it only reads.
    /// </para>
    /// </remarks>
    public static string? WhyItCannotBeWritten(string volumePath)
    {
        var directory = Path.Combine(volumePath, Directory);
        var probe = Path.Combine(directory, $".writable-{Guid.NewGuid():N}");

        try
        {
            System.IO.Directory.CreateDirectory(directory);

            using (File.Create(probe))
            {
            }

            File.Delete(probe);

            return null;
        }
        catch (Exception cause)
            when (cause is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return cause.Message;
        }
    }
}
