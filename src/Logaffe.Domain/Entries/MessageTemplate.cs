using System.Text;

namespace Logaffe.Domain.Entries;

/// <summary>
/// Turning the message a sender wrote into the message the operator reads.
/// </summary>
/// <remarks>
/// <para>
/// There is exactly one message field on the wire and it is always a template —
/// a plain sentence is one with no placeholders in it — so this is the only
/// place a rendered message comes from, and it runs once when the entry arrives
/// rather than each time it is read
/// (<see href="https://github.com/datavisionzero/logaffe/blob/main/docs/adr/0005-the-rendered-message-is-stored-not-recomputed.md">ADR 0005</see>).
/// </para>
/// <para>
/// <b>The rule is narrower than Serilog's, deliberately.</b> Only a placeholder
/// naming a property that was actually delivered is substituted. An unmatched
/// <c>{Foo}</c>, a doubled <c>{{</c>, an unclosed brace and every other brace
/// stay character for character as they arrived — including the <c>{{</c> that
/// Serilog reads as an escape. Log content is untrusted and routinely contains
/// text the application never wrote: an application logging a raw request body
/// will sooner or later log braces, and the ordinary escaping rule would
/// silently rewrite it
/// (<see href="https://github.com/datavisionzero/logaffe/blob/main/docs/adr/0004-the-ingestion-format-is-clef-and-the-server-renders.md">ADR 0004</see>).
/// Under this rule a plain line renders to itself byte for byte, which is what
/// makes the one-field <c>curl</c> case of <c>docs/ingestion.md</c> honest.
/// </para>
/// </remarks>
public static class MessageTemplate
{
    private const char Open = '{';
    private const char Close = '}';

    /// <summary>
    /// Serilog's destructuring hints. They say how the sender captured the
    /// value, not what it is called: the property arrives under the bare name,
    /// so the sigil is read past to find it.
    /// </summary>
    private const string Sigils = "@$";

    /// <summary>
    /// What separates a name from an alignment or a format specifier.
    /// </summary>
    /// <remarks>
    /// The specifier is used to find the name and then dropped rather than
    /// applied. logaffe stores values as they were delivered and does not hold
    /// the type a specifier would need to mean anything — <c>{Elapsed:0.000}</c>
    /// renders the number that arrived. Leaving the whole placeholder standing
    /// instead would put <c>{Elapsed:0.000}</c> on the operator's screen, which
    /// is the one reading that helps nobody.
    /// </remarks>
    private const string Specifiers = ",:";

    /// <summary>
    /// <paramref name="template"/> with the properties of
    /// <paramref name="properties"/> substituted into the placeholders that name
    /// them, and everything else left exactly as it arrived.
    /// </summary>
    public static string Render(
        string template, IReadOnlyDictionary<string, string> properties)
    {
        // A template with no holes, or a delivery with nothing to fill them: the
        // message is its own rendering, and the common case does no work.
        if (properties.Count == 0 || !template.Contains(Open, StringComparison.Ordinal))
        {
            return template;
        }

        var rendered = new StringBuilder(template.Length);
        var rest = template.AsSpan();

        while (!rest.IsEmpty)
        {
            var open = rest.IndexOf(Open);
            if (open < 0)
            {
                rendered.Append(rest);
                break;
            }

            rendered.Append(rest[..open]);
            rest = rest[open..];

            var close = rest.IndexOf(Close);
            if (close < 0)
            {
                // An opening brace with nothing closing it is text, and text is
                // kept.
                rendered.Append(rest);
                break;
            }

            var placeholder = rest[..(close + 1)];
            var name = NameIn(placeholder[1..^1]);

            if (name is not null && properties.TryGetValue(name, out var value))
            {
                rendered.Append(value);
            }
            else
            {
                // Verbatim through the closing brace, rather than reopening the
                // scan inside what was just refused: whatever is between those
                // braces is content, and a second pass over it would be the
                // rewriting this rule exists to avoid.
                rendered.Append(placeholder);
            }

            rest = rest[(close + 1)..];
        }

        return rendered.ToString();
    }

    /// <summary>
    /// The property name a placeholder's contents refer to, or <c>null</c> when
    /// they refer to none — which is every brace that was part of the message
    /// rather than part of a hole in it.
    /// </summary>
    private static string? NameIn(ReadOnlySpan<char> contents)
    {
        if (!contents.IsEmpty && Sigils.Contains(contents[0]))
        {
            contents = contents[1..];
        }

        var specifier = contents.IndexOfAny(Specifiers);
        if (specifier >= 0)
        {
            contents = contents[..specifier];
        }

        return contents.IsEmpty ? null : contents.ToString();
    }
}
