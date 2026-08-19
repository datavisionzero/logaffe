using Logaffe.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

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
        this ILoggingBuilder builder, Action<LogaffeLoggerOptions> configure) =>
        Sender(builder, configure, http: null);

    /// <inheritdoc cref="AddLogaffe(ILoggingBuilder, Action{LogaffeLoggerOptions})"/>
    /// <param name="http">
    /// The application's own client, which is not disposed with the provider.
    /// </param>
    /// <remarks>
    /// Here rather than only on the provider's constructor, because a provider
    /// built by the application is one the container will not dispose — and that
    /// is the flush on shutdown, so the entries an application ends with would be
    /// the ones it loses. Bringing a client is not a reason to give that up.
    /// </remarks>
    public static ILoggingBuilder AddLogaffe(
        this ILoggingBuilder builder, Action<LogaffeLoggerOptions> configure, HttpClient http)
    {
        ArgumentNullException.ThrowIfNull(http);

        return Sender(builder, configure, http);
    }

    /// <summary>
    /// One sender, registered so that the container owns it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Enumerable, because a provider is one of several an application logs
    /// through: logaffe is additive, and the file the application already writes
    /// is where a delivery that failed gets reported.
    /// </para>
    /// <para>
    /// <b>One call is one sender.</b> Added rather than added-if-absent, which
    /// would be de-duplicated by the provider's own type: a second call carries
    /// a second installation and a second project's token, and dropping it would
    /// drop both without a word. Two calls naming the same installation deliver
    /// twice instead, which the operator reads in the entries themselves — a
    /// configuration that was discarded is legible nowhere.
    /// </para>
    /// <para>
    /// Registered as a factory rather than as an instance, which is not a style
    /// choice: a container never disposes an object it did not create, so handing
    /// it a ready-made provider would mean the flush on shutdown never runs and
    /// an application's last entries never leave.
    /// </para>
    /// </remarks>
    private static ILoggingBuilder Sender(
        ILoggingBuilder builder, Action<LogaffeLoggerOptions> configure, HttpClient? http)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new LogaffeLoggerOptions();
        configure(options);

        builder.Services.Add(ServiceDescriptor.Singleton<ILoggerProvider>(
            _ => http is null
                ? new LogaffeLoggerProvider(options)
                : new LogaffeLoggerProvider(options, http)));

        return builder;
    }
}
