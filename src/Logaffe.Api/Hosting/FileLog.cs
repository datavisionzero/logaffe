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
    /// Bounded on purpose — it rolls by size and keeps a fixed number of files.
    /// An unbounded log on the same volume as the secrets is the most
    /// embarrassing possible way for a logging product to take its own
    /// installation down.
    /// </summary>
    public static LoggerConfiguration WriteToLogaffeFile(
        this LoggerSinkConfiguration sink, string volumePath) =>
        sink.File(
            Path.Combine(volumePath, "logs", "logaffe-.log"),
            rollingInterval: RollingInterval.Day,
            rollOnFileSizeLimit: true,
            fileSizeLimitBytes: 32L * 1024 * 1024,
            retainedFileCountLimit: 14,
            shared: true);
}
