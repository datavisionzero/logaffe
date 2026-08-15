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
