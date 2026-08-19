using Logaffe.Client;

namespace Logaffe.Extensions.Logging;

/// <summary>
/// What an application says once, so that everything after it is
/// <c>ILogger</c>.
/// </summary>
public sealed class LogaffeLoggerOptions
{
    /// <summary>
    /// Where the installation answers — scheme and host, as the operator reaches
    /// it. The ingest path is appended and is not a setting.
    /// </summary>
    public Uri? Installation { get; set; }

    /// <summary>
    /// The project's ingest token. A delivery never names a project, because the
    /// token is the project.
    /// </summary>
    public string? IngestToken { get; set; }

    /// <summary>
    /// What names this instance among the replicas of one service — a hostname
    /// or, in a container, the container id. <see langword="null"/> turns it
    /// off, and an entry that already carries an <c>instance</c> property keeps
    /// its own.
    /// </summary>
    /// <remarks>
    /// The same decision and the same default as the Serilog sink, so that the
    /// two packages do not differ in what an entry carries.
    /// </remarks>
    public string? Instance { get; set; } = Environment.MachineName;

    /// <summary>
    /// Whether the properties of the scopes an entry was written inside are
    /// carried with it. <b>Off by default.</b>
    /// </summary>
    /// <remarks>
    /// Following the convention the framework's own providers use, and off for
    /// logaffe's own reason rather than for taste: more than 64 properties makes
    /// an entry <em>invalid</em> rather than truncated, and nested scopes are
    /// the easiest way to cross that without noticing. An application that turns
    /// them on is choosing it.
    /// </remarks>
    public bool IncludeScopes { get; set; }

    /// <summary>
    /// How many entries may wait in memory before the oldest are dropped. Ten
    /// full batches, for the reason
    /// <see cref="EntryDeliveryOptions.QueueCapacity"/> gives.
    /// </summary>
    public int QueueCapacity { get; set; } = 10_000;

    /// <summary>
    /// How long the first entry of a batch waits for company before it is sent
    /// on its own.
    /// </summary>
    public TimeSpan BatchInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// How long shutdown spends trying to deliver what is still queued.
    /// </summary>
    public TimeSpan FlushTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>How long one delivery may take before it is abandoned.</summary>
    public TimeSpan DeliveryTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Where this reports what went wrong, which is the application's own local
    /// log.
    /// </summary>
    /// <remarks>
    /// It cannot be an <c>ILogger</c>: reporting a failed delivery through the
    /// logging stack this provider is part of would hand it straight back to
    /// this provider. Left unset it writes to <see cref="Console.Error"/>, which
    /// is where a container's own log is.
    /// </remarks>
    public Action<string, Exception?>? OnFailure { get; set; }

    internal EntryDeliveryOptions Delivery() => new()
    {
        Installation = Installation
            ?? throw new InvalidOperationException(
                "Logaffe: the installation's address is not configured."),
        IngestToken = IngestToken
            ?? throw new InvalidOperationException(
                "Logaffe: the ingest token is not configured."),
        QueueCapacity = QueueCapacity,
        BatchInterval = BatchInterval,
        FlushTimeout = FlushTimeout,
        DeliveryTimeout = DeliveryTimeout,

        // Left unset it stays unset, and the delivery underneath writes to
        // Console.Error itself. One fallback, in the one place that reports.
        OnFailure = OnFailure,
    };
}
