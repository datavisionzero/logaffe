using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Logaffe.Extensions.Logging;

/// <summary>
/// One CLEF line, built from what <c>ILogger</c> was handed.
/// </summary>
/// <remarks>
/// <para>
/// <b>The template is the whole point.</b> <c>docs/ingestion.md</c> is explicit
/// that the message is a template, that plain text is a template without holes,
/// and that the server renders and stores both (ADR 0004, ADR 0005). So the
/// original template goes across as <c>@mt</c> with the named state beside it,
/// rather than an already-formatted string — which would arrive as a template
/// with no holes, render to itself, and cost every filter that would have worked
/// on it.
/// </para>
/// <para>
/// The mechanism is the convention <c>Microsoft.Extensions.Logging</c> already
/// has: the state is an <c>IReadOnlyList&lt;KeyValuePair&lt;string, object?&gt;&gt;</c>
/// whose <c>{OriginalFormat}</c> entry is the template and whose other pairs are
/// the properties.
/// </para>
/// <para>
/// <b><c>@m</c> is never written.</b> logaffe refuses any entry carrying it —
/// the same trap the Serilog sink names, where picking the rendered formatter
/// fails every line.
/// </para>
/// </remarks>
internal static class ClefLine
{
    /// <summary>The key the framework puts the template under.</summary>
    private const string OriginalFormat = "{OriginalFormat}";

    /// <summary>The property logaffe promotes to the logger name.</summary>
    private const string SourceContext = "SourceContext";

    /// <inheritdoc cref="SourceContext"/>
    private const string Instance = "instance";

    /// <summary>
    /// The two logaffe promotes for correlation. They are ordinary properties
    /// rather than CLEF's <c>@tr</c> and <c>@sp</c>, which logaffe passes over.
    /// </summary>
    private const string TraceId = "TraceId";

    /// <inheritdoc cref="TraceId"/>
    private const string SpanId = "SpanId";

    public static string Write<TState>(
        DateTimeOffset at,
        LogLevel level,
        string category,
        EventId eventId,
        TState state,
        Exception? exception,
        string? instance,
        IExternalScopeProvider? scopes)
    {
        var buffer = new ArrayBufferWriter<byte>(512);
        using var json = new Utf8JsonWriter(buffer);

        // Which names are already spoken for. The entry's own state wins over a
        // scope, and a scope over what this provider would have added, because
        // the closer the writer is to the entry the more it meant by it.
        var written = new HashSet<string>(StringComparer.Ordinal);

        json.WriteStartObject();

        json.WriteString("@t", at.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));

        json.WriteString("@mt", Template(state) ?? state?.ToString() ?? string.Empty);

        // Written as Microsoft.Extensions.Logging spells it: `docs/ingestion.md`
        // accepts both spellings and maps them without loss, so there is no
        // mapping table here to write and get wrong. `Information` is left off,
        // because an absent level means exactly that.
        if (level is not LogLevel.Information && Named(level) is { } named)
        {
            json.WriteString("@l", named);
        }

        if (exception is not null)
        {
            // Whatever the runtime produced, stored as delivered and never
            // parsed. It is not folded into the message.
            json.WriteString("@x", exception.ToString());
        }

        Property(json, written, SourceContext, category);

        if (eventId.Id != 0)
        {
            // An ordinary property rather than CLEF's `@i`: logaffe passes over
            // `@` keys it does not know, so an `@i` would be silently dropped,
            // whereas a property is stored and filterable.
            Property(json, written, "EventId", eventId.Id);
        }

        if (!string.IsNullOrEmpty(eventId.Name))
        {
            Property(json, written, "EventName", eventId.Name);
        }

        foreach (var pair in Pairs(state))
        {
            if (pair.Key != OriginalFormat)
            {
                Property(json, written, pair.Key, pair.Value);
            }
        }

        scopes?.ForEachScope(
            (scope, _) =>
            {
                foreach (var pair in Pairs(scope))
                {
                    if (pair.Key != OriginalFormat)
                    {
                        Property(json, written, pair.Key, pair.Value);
                    }
                }
            },
            json);

        if (Activity.Current is { } activity)
        {
            Property(json, written, TraceId, activity.TraceId.ToString());
            Property(json, written, SpanId, activity.SpanId.ToString());
        }

        if (instance is not null)
        {
            Property(json, written, Instance, instance);
        }

        json.WriteEndObject();
        json.Flush();

        return System.Text.Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static string? Template<TState>(TState state) =>
        state is IReadOnlyList<KeyValuePair<string, object?>> pairs
            ? pairs.FirstOrDefault(pair => pair.Key == OriginalFormat).Value as string
            : null;

    private static IEnumerable<KeyValuePair<string, object?>> Pairs(object? state) =>
        state as IEnumerable<KeyValuePair<string, object?>> ?? [];

    private static string? Named(LogLevel level) => level switch
    {
        LogLevel.Trace => "Trace",
        LogLevel.Debug => "Debug",
        LogLevel.Warning => "Warning",
        LogLevel.Error => "Error",
        LogLevel.Critical => "Critical",
        _ => null,
    };

    private static void Property(
        Utf8JsonWriter json, HashSet<string> written, string name, object? value)
    {
        // Nothing may start with `@`, which is CLEF's own space: a property
        // called `@t` would not be a property, it would be a second timestamp.
        if (name.Length == 0 || name[0] == '@' || !written.Add(name))
        {
            return;
        }

        json.WritePropertyName(name);

        Value(json, value);
    }

    /// <summary>
    /// Scalars as themselves and everything else as its text.
    /// </summary>
    /// <remarks>
    /// logaffe stores values as they arrived and holds no type for anything to
    /// mean against, so a number is worth writing as a number — it is what makes
    /// the property filterable — and an arbitrary object is not worth
    /// serializing into a shape nobody will query. What it is called
    /// <c>ToString</c> for is what an operator would have seen anyway.
    /// </remarks>
    private static void Value(Utf8JsonWriter json, object? value)
    {
        switch (value)
        {
            case null:
                json.WriteNullValue();
                break;
            case string text:
                json.WriteStringValue(text);
                break;
            case bool yes:
                json.WriteBooleanValue(yes);
                break;
            case byte or sbyte or short or ushort or int or uint or long:
                json.WriteNumberValue(Convert.ToInt64(value, CultureInfo.InvariantCulture));
                break;
            case ulong number:
                json.WriteNumberValue(number);
                break;
            case float or double:
                json.WriteNumberValue(Convert.ToDouble(value, CultureInfo.InvariantCulture));
                break;
            case decimal number:
                json.WriteNumberValue(number);
                break;
            case DateTime moment:
                json.WriteStringValue(moment);
                break;
            case DateTimeOffset moment:
                json.WriteStringValue(moment);
                break;
            case Guid id:
                json.WriteStringValue(id);
                break;
            default:
                json.WriteStringValue(
                    value as string
                    ?? (value is IFormattable formattable
                        ? formattable.ToString(null, CultureInfo.InvariantCulture)
                        : value.ToString()));
                break;
        }
    }
}
