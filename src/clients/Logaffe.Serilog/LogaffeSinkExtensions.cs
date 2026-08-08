using Logaffe.Client;
using Serilog.Configuration;
using Serilog.Core;
using Serilog.Debugging;
using Serilog.Events;

namespace Serilog;

/// <summary>
/// <c>WriteTo.Logaffe(…)</c>.
/// </summary>
/// <remarks>
/// In Serilog's own namespace, which is where a sink's configuration method has
/// to be for <c>WriteTo</c> to find it. The package keeps logaffe's prefix
/// rather than reaching into Serilog's, which is reserved by the project that
/// owns it (<c>docs/codebase.md</c>).
/// </remarks>
public static class LogaffeSinkExtensions
{
    /// <summary>
    /// Delivers to a logaffe installation, naming this instance by the machine
    /// it runs on.
    /// </summary>
    /// <param name="to">The sink configuration this extends.</param>
    /// <param name="installation">
    /// Where the installation answers — scheme and host, as the operator reaches
    /// it. The ingest path is appended and is not a setting.
    /// </param>
    /// <param name="ingestToken">
    /// The project's ingest token. A delivery never names a project, because the
    /// token is the project.
    /// </param>
    /// <param name="restrictedToMinimumLevel">
    /// The floor for this sink alone, if the logger's own is not the one wanted
    /// here.
    /// </param>
    /// <param name="levelSwitch">A floor that can be moved while running.</param>
    public static LoggerConfiguration Logaffe(
        this LoggerSinkConfiguration to,
        Uri installation,
        string ingestToken,
        LogEventLevel restrictedToMinimumLevel = LevelAlias.Minimum,
        LoggingLevelSwitch? levelSwitch = null) =>
        to.Logaffe(
            new EntryDeliveryOptions
            {
                Installation = installation,
                IngestToken = ingestToken,

                // Serilog's own channel for what a sink cannot report through
                // the logger it is part of. Reporting a failed delivery through
                // Serilog would hand it back to this sink.
                OnFailure = (message, exception) => SelfLog.WriteLine(
                    "logaffe: {0}{1}",
                    message,
                    exception is null ? string.Empty : $" {exception}"),
            },
            restrictedToMinimumLevel,
            levelSwitch);

    /// <inheritdoc cref="Logaffe(LoggerSinkConfiguration, Uri, string, LogEventLevel, LoggingLevelSwitch)"/>
    /// <param name="delivery">
    /// Everything about the delivery: the address and the token, and the queue,
    /// batching and flush settings a sender may want to move.
    /// </param>
    public static LoggerConfiguration Logaffe(
        this LoggerSinkConfiguration to,
        EntryDeliveryOptions delivery,
        LogEventLevel restrictedToMinimumLevel = LevelAlias.Minimum,
        LoggingLevelSwitch? levelSwitch = null) =>
        to.Logaffe(delivery, Environment.MachineName, restrictedToMinimumLevel, levelSwitch);

    /// <inheritdoc cref="Logaffe(LoggerSinkConfiguration, EntryDeliveryOptions, LogEventLevel, LoggingLevelSwitch)"/>
    /// <param name="instance">
    /// What names this instance among the replicas of one service — a hostname
    /// or a container id. <see langword="null"/> leaves it off entirely, and an
    /// event that already carries an <c>instance</c> property keeps its own.
    /// </param>
    /// <remarks>
    /// The instance is an overload rather than an optional argument because a
    /// default of <see cref="Environment.MachineName"/> cannot be written in a
    /// signature, and because <see langword="null"/> has to keep meaning "off"
    /// rather than "unspecified".
    /// </remarks>
    public static LoggerConfiguration Logaffe(
        this LoggerSinkConfiguration to,
        EntryDeliveryOptions delivery,
        string? instance,
        LogEventLevel restrictedToMinimumLevel = LevelAlias.Minimum,
        LoggingLevelSwitch? levelSwitch = null) =>
        to.Sink(
            new global::Logaffe.Serilog.LogaffeSink(delivery, instance), restrictedToMinimumLevel, levelSwitch);
}
