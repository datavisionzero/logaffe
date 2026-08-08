using Logaffe.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.Logging;

/// <summary>
/// <c>builder.Logging.AddLogaffe(…)</c>.
/// </summary>
/// <remarks>
/// In the framework's namespace, which is where an application expects to find
/// <c>AddSomething</c> on its logging builder. The package itself keeps
/// logaffe's own prefix rather than reaching into anybody else's
/// (<c>docs/codebase.md</c>).
/// </remarks>
public static class LogaffeLoggingBuilderExtensions
{
    /// <summary>
    /// Delivers every entry the application's filters let through to a logaffe
    /// installation.
    /// </summary>
    /// <param name="builder">The logging builder this extends.</param>
    /// <param name="installation">
    /// Where the installation answers — scheme and host, as the operator reaches
    /// it. The ingest path is appended and is not a setting.
    /// </param>
    /// <param name="ingestToken">
    /// The project's ingest token. A delivery never names a project, because the
    /// token is the project.
    /// </param>
    public static ILoggingBuilder AddLogaffe(
        this ILoggingBuilder builder, Uri installation, string ingestToken) =>
        builder.AddLogaffe(options =>
        {
            options.Installation = installation;
            options.IngestToken = ingestToken;
        });

    /// <inheritdoc cref="AddLogaffe(ILoggingBuilder, Uri, string)"/>
    /// <param name="configure">
    /// Everything else: the instance name, whether scopes are carried, and what
    /// the queue, the batching and the flush do.
    /// </param>
    public static ILoggingBuilder AddLogaffe(
        this ILoggingBuilder builder, Action<LogaffeLoggerOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new LogaffeLoggerOptions();
        configure(options);

        // Enumerable, because a provider is one of several an application logs
        // through: logaffe is additive, and the file the application already
        // writes is where a delivery that failed gets reported.
        //
        // Registered as a factory rather than as an instance, which is not a
        // style choice: a container never disposes an object it did not create,
        // so handing it a ready-made provider would mean the flush on shutdown
        // never runs and an application's last entries never leave.
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<ILoggerProvider, LogaffeLoggerProvider>(
                _ => new LogaffeLoggerProvider(options)));

        return builder;
    }
}
