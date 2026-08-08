using Logaffe.Client;
using Microsoft.Extensions.Logging;

namespace Logaffe.Extensions.Logging;

/// <summary>
/// An <c>ILoggerProvider</c> delivering to a logaffe installation, for
/// applications that are not on Serilog.
/// </summary>
/// <remarks>
/// It builds the same CLEF the Serilog sink does and hands it to the same
/// <see cref="EntryDelivery"/>, which is where everything about queueing,
/// dropping, not throwing, not blocking and flushing lives. That is what makes
/// the two packages behave identically under stress, as
/// <c>docs/ingestion.md</c> requires — neither of them owns that behaviour.
/// <para>
/// There is no <c>ProviderAlias</c> on it, which would have meant depending on
/// <c>Microsoft.Extensions.Logging</c> itself for a shorter name in
/// <c>appsettings.json</c>. A filter can still name this provider by its type,
/// and asking nothing of the application's logging stack is worth more than the
/// spelling.
/// </para>
/// </remarks>
public sealed class LogaffeLoggerProvider : ILoggerProvider, ISupportExternalScope
{
    private readonly EntryDelivery _delivery;
    private readonly LogaffeLoggerOptions _options;

    private IExternalScopeProvider? _scopes;

    public LogaffeLoggerProvider(LogaffeLoggerOptions options)
    {
        _options = options;
        _delivery = new EntryDelivery(options.Delivery());
    }

    /// <summary>
    /// Delivers over a caller's <see cref="HttpClient"/>, which is not disposed
    /// with this.
    /// </summary>
    public LogaffeLoggerProvider(LogaffeLoggerOptions options, HttpClient http)
    {
        _options = options;
        _delivery = new EntryDelivery(options.Delivery(), http);
    }

    public ILogger CreateLogger(string categoryName) =>
        new LogaffeLogger(categoryName, this);

    /// <summary>
    /// Called by the logging factory, which owns the scopes rather than each
    /// provider keeping its own.
    /// </summary>
    public void SetScopeProvider(IExternalScopeProvider scopeProvider) =>
        _scopes = scopeProvider;

    /// <summary>
    /// Gives what is queued <see cref="EntryDeliveryOptions.FlushTimeout"/> to
    /// leave, which is the flush on shutdown the promise names.
    /// </summary>
    public void Dispose() => _delivery.Dispose();

    internal void Send(string clefLine) => _delivery.Send(clefLine);

    internal string? Instance => _options.Instance;

    /// <summary>
    /// The scopes an entry was written inside, or nothing when the application
    /// has not asked for them.
    /// </summary>
    internal IExternalScopeProvider? Scopes => _options.IncludeScopes ? _scopes : null;

    private sealed class LogaffeLogger(string category, LogaffeLoggerProvider provider) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull =>
            provider._scopes?.Push(state);

        /// <summary>
        /// Everything the filters let through. What an installation is worth
        /// being sent is the application's configuration to make, and this
        /// provider is not a second place to make it.
        /// </summary>
        public bool IsEnabled(LogLevel logLevel) => logLevel is not LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            // The formatter is what would have produced the rendered message,
            // and it is deliberately not called: the server renders (ADR 0005),
            // and a rendered message here would arrive as `@m` and be refused.
            provider.Send(ClefLine.Write(
                DateTimeOffset.UtcNow,
                logLevel,
                category,
                eventId,
                state,
                exception,
                provider.Instance,
                provider.Scopes));
        }
    }
}
