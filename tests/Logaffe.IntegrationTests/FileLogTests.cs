using Logaffe.Api.Hosting;
using Logaffe.Application.Operations;
using Serilog;

namespace Logaffe.IntegrationTests;

/// <summary>
/// Where logaffe's own log lands on the volume, asked of the code that writes it
/// rather than of the string that names it.
/// </summary>
/// <remarks>
/// A backup carries the volume except for that directory
/// (<see cref="TakeABackup.LogDirectory"/>), and the two say so in different
/// projects that cannot reference each other. Moving the log without moving the
/// exclusion would put it back into every artifact, silently and in a way only
/// the size would show. This is the test that fails instead.
/// </remarks>
public sealed class FileLogTests
{
    /// <summary>
    /// A volume that takes the log is not complained about.
    /// </summary>
    [Fact]
    public void A_volume_that_can_be_written_has_nothing_said_about_it()
    {
        var volume = Directory.CreateTempSubdirectory("logaffe-writable");

        try
        {
            Assert.Null(FileLog.WhyItCannotBeWritten(volume.FullName));
        }
        finally
        {
            volume.Delete(recursive: true);
        }
    }

    /// <summary>
    /// One that does not is, and with the reason on it.
    /// </summary>
    /// <remarks>
    /// A file where the log directory belongs, rather than a permission taken
    /// away: it is the same answer on every operating system and to a process
    /// running as root, which a mode of 500 is not. What is being asked is
    /// whether the check writes to find out, and it is — a directory that cannot
    /// be made is exactly what a read-only volume gives it.
    /// </remarks>
    [Fact]
    public void A_volume_that_cannot_be_written_is_said_out_loud()
    {
        var volume = Directory.CreateTempSubdirectory("logaffe-unwritable");

        try
        {
            // The constant is the prefix a backup filters on, so it carries the
            // separator a directory name does not.
            File.WriteAllText(
                Path.Combine(volume.FullName, TakeABackup.LogDirectory.TrimEnd('/')),
                string.Empty);

            Assert.NotNull(FileLog.WhyItCannotBeWritten(volume.FullName));
        }
        finally
        {
            volume.Delete(recursive: true);
        }
    }

    /// <summary>
    /// Asking leaves nothing behind on the volume it asked about.
    /// </summary>
    [Fact]
    public void The_question_takes_its_own_file_back()
    {
        var volume = Directory.CreateTempSubdirectory("logaffe-probe");

        try
        {
            FileLog.WhyItCannotBeWritten(volume.FullName);

            Assert.Empty(Directory.EnumerateFiles(
                volume.FullName, "*", SearchOption.AllDirectories));
        }
        finally
        {
            volume.Delete(recursive: true);
        }
    }

    [Fact]
    public void The_log_lands_in_the_directory_a_backup_leaves_behind()
    {
        var volume = Directory.CreateTempSubdirectory("logaffe-filelog");

        try
        {
            using (var log = new LoggerConfiguration()
                .WriteTo.WriteToLogaffeFile(volume.FullName)
                .CreateLogger())
            {
                log.Information("a line, so that there is a file at all");
            }

            var written = Directory
                .EnumerateFiles(volume.FullName, "*", SearchOption.AllDirectories)
                .Select(path => Path
                    .GetRelativePath(volume.FullName, path)
                    .Replace(Path.DirectorySeparatorChar, '/'))
                .ToArray();

            Assert.NotEmpty(written);
            Assert.All(written, file =>
                Assert.StartsWith(TakeABackup.LogDirectory, file, StringComparison.Ordinal));
        }
        finally
        {
            volume.Delete(recursive: true);
        }
    }
}
