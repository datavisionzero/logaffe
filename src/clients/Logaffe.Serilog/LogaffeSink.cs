using System.Globalization;
using System.Text;
using Logaffe.Client;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Compact;

namespace Logaffe.Serilog;

/// <summary>
/// A Serilog sink that is <see cref="CompactJsonFormatter"/> pointed at a
/// logaffe installation.
/// </summary>
/// <remarks>
/// <para>
/// Because the ingestion format is CLEF
/// (<see href="https://github.com/datavisionzero/logaffe/blob/main/docs/adr/0004-the-ingestion-format-is-clef-and-the-server-renders.md">ADR 0004</see>),
/// this is configuration rather than a mapping layer: the formatter writes the
/// line, <see cref="EntryDelivery"/> delivers it, and everything about queueing,
/// dropping, not throwing, not blocking and flushing lives there — which is what
/// makes this sink and the <c>ILoggerProvider</c> beside it behave identically
/// under stress.
/// </para>
/// <para>
/// <b>Never <c>RenderedCompactJsonFormatter</c>.</b> That one writes <c>@m</c>,
/// and logaffe refuses any entry carrying it — an integration in which every
/// single line is counted invalid and nothing is stored. There is one right
/// answer here and picking the other fails quietly, per entry rather than per
/// delivery.
/// </para>
/// </remarks>
public sealed class LogaffeSink : ILogEventSink, IDisposable
{
    /// <summary>
    /// The property logaffe promotes to a first-class field, and what makes
    /// three replicas of one service separable in the UI and over MCP.
    /// </summary>
    public const string InstanceProperty = "instance";

    /// <summary>
    /// The two logaffe promotes for correlation. Serilog carries them on the
    /// event rather than among its properties, and
    /// <see cref="CompactJsonFormatter"/> writes them as CLEF's <c>@tr</c> and
    /// <c>@sp</c> — keys logaffe passes over. Written as properties as well, so
    /// that an entry can be found by the request it belongs to with nothing
    /// asked of the application.
    /// </summary>
    public const string TraceProperty = "TraceId";

    /// <inheritdoc cref="TraceProperty"/>
    public const string SpanProperty = "SpanId";

    private readonly ITextFormatter _clef = new CompactJsonFormatter();
    private readonly EntryDelivery _delivery;
    private readonly string? _instance;

    public LogaffeSink(EntryDeliveryOptions delivery, string? instance)
    {
        _delivery = new EntryDelivery(delivery);
        _instance = instance;
    }

    /// <summary>
    /// Delivers over a caller's <see cref="HttpClient"/>, which is not disposed
    /// with this.
    /// </summary>
    public LogaffeSink(EntryDeliveryOptions delivery, string? instance, HttpClient http)
    {
        _delivery = new EntryDelivery(delivery, http);
        _instance = instance;
    }

    public void Emit(LogEvent logEvent)
    {
        if (logEvent is null)
        {
            return;
        }

        var line = new StringWriter(new StringBuilder(256), CultureInfo.InvariantCulture);

        _clef.Format(Promoted(logEvent), line);

        _delivery.Send(line.ToString());
    }

    public void Dispose() => _delivery.Dispose();

    /// <summary>
    /// The same event with the properties logaffe promotes, where the event does
    /// not already carry them.
    /// </summary>
    /// <remarks>
    /// A copy rather than <c>AddPropertyIfAbsent</c> on the event itself: the
    /// event belongs to the logger and every other sink is holding the same one,
    /// so enriching it here would put this sink's decisions into the
    /// application's console.
    /// </remarks>
    private LogEvent Promoted(LogEvent logEvent)
    {
        var added = Additions(logEvent);

        if (added.Count == 0)
        {
            return logEvent;
        }

        return new LogEvent(
            logEvent.Timestamp,
            logEvent.Level,
            logEvent.Exception,
            logEvent.MessageTemplate,
            [.. logEvent.Properties.Select(p => new LogEventProperty(p.Key, p.Value)), .. added],
            logEvent.TraceId ?? default,
            logEvent.SpanId ?? default);
    }

    private List<LogEventProperty> Additions(LogEvent logEvent)
    {
        var added = new List<LogEventProperty>(3);

        // Only when the event does not already carry one: an application that
        // names its own instances means something by it.
        if (_instance is not null && !logEvent.Properties.ContainsKey(InstanceProperty))
        {
            added.Add(new LogEventProperty(InstanceProperty, new ScalarValue(_instance)));
        }

        if (logEvent.TraceId is { } trace && !logEvent.Properties.ContainsKey(TraceProperty))
        {
            added.Add(new LogEventProperty(TraceProperty, new ScalarValue(trace.ToString())));
        }

        if (logEvent.SpanId is { } span && !logEvent.Properties.ContainsKey(SpanProperty))
        {
            added.Add(new LogEventProperty(SpanProperty, new ScalarValue(span.ToString())));
        }

        return added;
    }
}
