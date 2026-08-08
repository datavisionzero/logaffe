using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Logaffe.Domain.Entries;

namespace Logaffe.Application.Operations;

/// <summary>
/// One line of a delivery, read: everything an entry carries except the three
/// things the installation supplies rather than the sender — the identity, the
/// project, and the clock that says when the batch arrived.
/// </summary>
/// <remarks>
/// <para>
/// The format is newline-delimited CLEF, adopted rather than invented
/// (<see href="https://github.com/datavisionzero/logaffe/blob/main/docs/adr/0004-the-ingestion-format-is-clef-and-the-server-renders.md">ADR 0004</see>).
/// Keys beginning with <c>@</c> are the entry's own fields and every other key is
/// a property. An <c>@</c> key logaffe does not know — CLEF's event id, its
/// renderings — is neither a field nor a property and is passed over: it is not
/// on the list of things that make an entry invalid, and treating a format's own
/// growth as a defect in the sender would be the wrong way round.
/// </para>
/// <para>
/// This is where <c>docs/ingestion.md</c> becomes a decision about one line, and
/// there are only two of them: the entry is read, or it is counted with a reason
/// (<see href="https://github.com/datavisionzero/logaffe/blob/main/docs/adr/0006-a-batch-is-accepted-in-part.md">ADR 0006</see>).
/// Nothing here refuses a batch — a line is never worth the other 999.
/// </para>
/// </remarks>
public sealed record ClefLine
{
    /// <summary>The sender's own field names, which are the whole of CLEF here.</summary>
    private const string EventTimeKey = "@t";
    private const string LevelKey = "@l";
    private const string MessageTemplateKey = "@mt";
    private const string RenderedMessageKey = "@m";
    private const string ExceptionKey = "@x";

    /// <summary>
    /// The four properties that become columns. They are ordinary CLEF
    /// properties with names logaffe recognizes; promotion asks nothing of a
    /// sender, and a delivery carrying none of them is complete.
    /// </summary>
    private const string InstanceKey = "instance";
    private const string LoggerNameKey = "SourceContext";
    private const string TraceIdKey = "TraceId";
    private const string SpanIdKey = "SpanId";

    /// <summary>The prefix that says a key is the entry's own field.</summary>
    private const char Reserved = '@';

    public required DateTimeOffset EventTime { get; init; }

    public required Level Level { get; init; }

    public required string MessageTemplate { get; init; }

    public required string RenderedMessage { get; init; }

    public string? Exception { get; init; }

    public string? Properties { get; init; }

    public string? LoggerName { get; init; }

    public string? Instance { get; init; }

    public byte[]? TraceId { get; init; }

    public byte[]? SpanId { get; init; }

    public required bool MessageTruncated { get; init; }

    public required bool ExceptionTruncated { get; init; }

    /// <summary>
    /// Reads one line, or says in one clause why it is not an entry — which is
    /// what the response body carries against its line number, for the person
    /// wiring up a new integration with <c>curl</c>.
    /// </summary>
    public static bool TryRead(
        ReadOnlyMemory<byte> line, out ClefLine read, out string reason)
    {
        read = null!;

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(line);
        }
        catch (JsonException)
        {
            reason = "the line is not JSON";
            return false;
        }

        using (document)
        {
            return TryRead(document.RootElement, out read, out reason);
        }
    }

    private static bool TryRead(JsonElement entry, out ClefLine read, out string reason)
    {
        read = null!;

        if (entry.ValueKind is not JsonValueKind.Object)
        {
            reason = "the line is not a JSON object";
            return false;
        }

        // Refused rather than ignored, so that there stays one place where
        // rendering happens and it is the server. Accepting it would put
        // rendering in two and raise the question of which wins when the two
        // disagree (ADR 0004).
        if (entry.TryGetProperty(RenderedMessageKey, out _))
        {
            reason = $"{RenderedMessageKey} is present; the server renders {MessageTemplateKey}";
            return false;
        }

        if (!entry.TryGetProperty(MessageTemplateKey, out var template)
            || template.ValueKind is not JsonValueKind.String)
        {
            reason = $"{MessageTemplateKey} is missing";
            return false;
        }

        if (!TryReadEventTime(entry, out var eventTime, out reason)
            || !TryReadLevel(entry, out var level, out reason))
        {
            return false;
        }

        if (!TryReadProperties(entry, out var properties, out reason))
        {
            return false;
        }

        var messageTemplate = template.GetString()!;

        // Qualified because this record carries a property of that name: the
        // template is the thing, and the type below is the rule about it.
        var (renderedMessage, messageTruncated) = Caps.CutTo(
            Domain.Entries.MessageTemplate.Render(messageTemplate, properties.Values),
            Caps.RenderedMessageBytes);

        var (exception, exceptionTruncated) = Caps.CutTo(
            entry.TryGetProperty(ExceptionKey, out var thrown)
            && thrown.ValueKind is JsonValueKind.String
                ? thrown.GetString()
                : null,
            Caps.ExceptionBytes);

        read = new ClefLine
        {
            EventTime = eventTime,
            Level = level,
            MessageTemplate = messageTemplate,

            // Cutting a message can only ever shorten it, so the non-null it
            // went in as is the non-null it comes back as.
            RenderedMessage = renderedMessage!,
            Exception = exception,
            Properties = properties.Kept,
            LoggerName = properties.LoggerName,
            Instance = properties.Instance,
            TraceId = properties.TraceId,
            SpanId = properties.SpanId,
            MessageTruncated = messageTruncated,
            ExceptionTruncated = exceptionTruncated,
        };

        return true;
    }

    /// <summary>
    /// <c>@t</c>, which is required and must carry an offset or <c>Z</c>. A
    /// local time without one is invalid rather than assumed to be anything:
    /// the two clocks of ADR 0007 are only worth having apart if the sender's
    /// is a moment and not a moment in an unnamed place.
    /// </summary>
    private static bool TryReadEventTime(
        JsonElement entry, out DateTimeOffset eventTime, out string reason)
    {
        eventTime = default;

        if (!entry.TryGetProperty(EventTimeKey, out var stamped)
            || stamped.ValueKind is not JsonValueKind.String)
        {
            reason = $"{EventTimeKey} is missing";
            return false;
        }

        var text = stamped.GetString()!;

        // Two parses, because the first will not say whether a zone was there:
        // it reads a zoneless time as local and hands back an offset that was
        // never delivered. The second is only asked what kind of moment it
        // was, and Unspecified is exactly the one being refused.
        if (!DateTimeOffset.TryParse(
                text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out eventTime)
            || !DateTime.TryParse(
                text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var zoned)
            || zoned.Kind is DateTimeKind.Unspecified)
        {
            reason = $"{EventTimeKey} is not a timestamp with an offset or Z";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    /// <summary>
    /// <c>@l</c>, which is optional. An absent one is
    /// <see cref="Levels.WhenAbsent"/>, which is what keeps the <c>curl</c> case
    /// short; an unrecognized one is refused rather than coerced, because a
    /// wrong level is worse than a counted rejection the operator can see.
    /// </summary>
    private static bool TryReadLevel(JsonElement entry, out Level level, out string reason)
    {
        reason = string.Empty;

        if (!entry.TryGetProperty(LevelKey, out var named))
        {
            level = Levels.WhenAbsent;
            return true;
        }

        if (named.ValueKind is JsonValueKind.String && Levels.TryParse(named.GetString(), out level))
        {
            return true;
        }

        level = default;
        reason = $"{LevelKey} is not a level";
        return false;
    }

    /// <summary>
    /// Everything that is not one of the entry's own fields: what the template
    /// renders against, what is lifted into a column, and what is kept as the
    /// object it arrived as.
    /// </summary>
    private static bool TryReadProperties(
        JsonElement entry, out DeliveredProperties properties, out string reason)
    {
        properties = default;
        reason = string.Empty;

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var kept = new List<JsonProperty>();
        string? instance = null;
        string? loggerName = null;
        byte[]? traceId = null;
        byte[]? spanId = null;

        foreach (var property in entry.EnumerateObject())
        {
            if (property.Name.StartsWith(Reserved))
            {
                continue;
            }

            if (values.Count == Caps.PropertiesPerEntry)
            {
                reason = $"more than {Caps.PropertiesPerEntry} properties";
                return false;
            }

            // Nothing in this product reads inside a property (ADR 0010), so
            // depth buys nothing here and an unbounded one is a parser handed
            // arbitrary nesting by an untrusted line.
            if (!IsWithinNesting(property.Value, Caps.PropertyNesting))
            {
                reason = $"the property {property.Name} is nested "
                    + $"more than {Caps.PropertyNesting} level deep";
                return false;
            }

            // Rendered against before promotion, so a template naming a promoted
            // property still fills: it was delivered, and that is the whole of
            // the rule (ADR 0004).
            values[property.Name] = TextOf(property.Value);

            // A promoted value leaves the object it arrived in — it is the same
            // value in a column now, and carrying it twice would show it twice.
            // A value that cannot be promoted stays an ordinary property, which
            // is the case a trace that is not sixteen well-formed bytes lands
            // in.
            var promoted = property.Name switch
            {
                InstanceKey => TryPromote(property.Value, ref instance),
                LoggerNameKey => TryPromote(property.Value, ref loggerName),
                TraceIdKey => TryPromote(property.Value, LogEntry.TraceIdLength, ref traceId),
                SpanIdKey => TryPromote(property.Value, LogEntry.SpanIdLength, ref spanId),
                _ => false,
            };

            if (!promoted)
            {
                kept.Add(property);
            }
        }

        properties = new DeliveredProperties(
            values, Serialize(kept), loggerName, instance, traceId, spanId);

        return true;
    }

    /// <summary>
    /// A promoted string: the value, or nothing when it is not a string at all.
    /// </summary>
    private static bool TryPromote(JsonElement value, ref string? promoted)
    {
        if (value.ValueKind is not JsonValueKind.String)
        {
            return false;
        }

        promoted = value.GetString();
        return true;
    }

    /// <summary>
    /// A promoted identifier, as the bytes it is rather than the hex it arrived
    /// as — which halves the column and every key in the index over it.
    /// Promotion requires a well-formed value, so a sender delivering something
    /// that is not one keeps it as an ordinary property rather than having it
    /// accepted into a column promising a shape it does not have.
    /// </summary>
    private static bool TryPromote(JsonElement value, int bytes, ref byte[]? promoted)
    {
        if (value.ValueKind is not JsonValueKind.String)
        {
            return false;
        }

        var hex = value.GetString();
        if (hex is null || hex.Length != bytes * 2)
        {
            return false;
        }

        foreach (var digit in hex)
        {
            if (!char.IsAsciiHexDigit(digit))
            {
                return false;
            }
        }

        promoted = Convert.FromHexString(hex);
        return true;
    }

    /// <summary>
    /// What a placeholder naming this property renders to. A string renders as
    /// its own text and everything else as the JSON it arrived as, which is the
    /// only rendering that is also "stored as delivered".
    /// </summary>
    private static string TextOf(JsonElement value) =>
        value.ValueKind is JsonValueKind.String ? value.GetString()! : value.GetRawText();

    /// <summary>
    /// The properties that were not promoted, as the object the column holds, or
    /// <c>null</c> when an entry carried none. The column is <c>jsonb</c>, so
    /// this text is the last form in which key order and whitespace exist.
    /// </summary>
    private static string? Serialize(List<JsonProperty> properties)
    {
        if (properties.Count == 0)
        {
            return null;
        }

        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            foreach (var property in properties)
            {
                property.WriteTo(writer);
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static bool IsWithinNesting(JsonElement value, int depth) => value.ValueKind switch
    {
        JsonValueKind.Object => depth > 0
            && value.EnumerateObject().All(inner => IsWithinNesting(inner.Value, depth - 1)),
        JsonValueKind.Array => depth > 0
            && value.EnumerateArray().All(inner => IsWithinNesting(inner, depth - 1)),
        _ => true,
    };

    /// <summary>
    /// The properties of one entry, in the three forms the read needs them: what
    /// the template renders against, what the column keeps, and what became a
    /// column of its own.
    /// </summary>
    private readonly record struct DeliveredProperties(
        IReadOnlyDictionary<string, string> Values,
        string? Kept,
        string? LoggerName,
        string? Instance,
        byte[]? TraceId,
        byte[]? SpanId);
}
