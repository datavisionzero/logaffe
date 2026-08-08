using System.Text;
using System.Text.Json;
using Logaffe.Application.Operations;
using Logaffe.Domain.Entries;

namespace Logaffe.UnitTests.Application;

/// <summary>
/// One line of a delivery, read.
/// </summary>
/// <remarks>
/// <c>docs/ingestion.md</c> names five things that make an entry invalid and
/// nothing else, so the tests below come in two halves: those five, and the
/// larger set of things a sender may do that must <b>not</b> cost them the
/// entry — an absent level, an unknown <c>@</c> key, a trace that is not one, a
/// property nobody has a placeholder for.
/// </remarks>
public sealed class ClefLineTests
{
    private const string Happened = "2026-08-07T09:15:00.417Z";

    [Fact]
    public void The_smallest_entry_the_format_allows_is_an_entry()
    {
        // What the delivery snippet sends, less the property: a clock and a
        // template, and nothing else asked of the sender.
        var entry = Read($$"""{"@t":"{{Happened}}","@mt":"Disk full on /dev/sda1"}""");

        Assert.Equal(new DateTimeOffset(2026, 8, 7, 9, 15, 0, 417, TimeSpan.Zero), entry.EventTime);
        Assert.Equal("Disk full on /dev/sda1", entry.MessageTemplate);
        Assert.Equal("Disk full on /dev/sda1", entry.RenderedMessage);
        Assert.Null(entry.Exception);
        Assert.Null(entry.Properties);
        Assert.False(entry.MessageTruncated);
        Assert.False(entry.ExceptionTruncated);
    }

    [Fact]
    public void An_absent_level_is_Information()
    {
        // Per CLEF, and it is what keeps the `curl` case short.
        Assert.Equal(Levels.WhenAbsent, Read(Line()).Level);
        Assert.Equal(Level.Information, Levels.WhenAbsent);
    }

    [Theory]
    [InlineData("Warning", Level.Warning)]
    [InlineData("warning", Level.Warning)]
    [InlineData("VERBOSE", Level.Verbose)]

    // The two names Microsoft.Extensions.Logging uses for the ends of the same
    // scale. Both spellings are accepted, and neither is a mapping layer.
    [InlineData("Trace", Level.Verbose)]
    [InlineData("Critical", Level.Fatal)]
    public void Both_spellings_of_a_level_are_read(string named, Level level) =>
        Assert.Equal(level, Read(Line($""" ,"@l":"{named}" """)).Level);

    [Fact]
    public void The_template_is_rendered_against_the_properties_that_were_delivered()
    {
        var entry = Read(
            $$"""
            {"@t":"{{Happened}}","@mt":"User {UserId} failed login from {Ip}","UserId":42,"Ip":"203.0.113.7"}
            """);

        Assert.Equal("User {UserId} failed login from {Ip}", entry.MessageTemplate);
        Assert.Equal("User 42 failed login from 203.0.113.7", entry.RenderedMessage);
    }

    [Fact]
    public void A_property_is_kept_as_the_object_it_arrived_as()
    {
        var entry = Read($$"""{"@t":"{{Happened}}","@mt":"Hello","UserId":42,"Ok":true}""");

        Assert.Equal("""{"UserId":42,"Ok":true}""", entry.Properties);
    }

    [Fact]
    public void An_exception_is_stored_as_delivered_and_never_folded_into_the_message()
    {
        var entry = Read(
            $$"""{"@t":"{{Happened}}","@mt":"Disk full","@x":"System.IO.IOException: no space"}""");

        Assert.Equal("System.IO.IOException: no space", entry.Exception);
        Assert.Equal("Disk full", entry.RenderedMessage);
    }

    [Fact]
    public void The_four_promoted_properties_become_fields_and_leave_the_object()
    {
        var entry = Read(
            $$"""
            {"@t":"{{Happened}}","@mt":"Handled","instance":"api-7c4f","SourceContext":"Orders.Api",
             "TraceId":"4bf92f3577b34da6a3ce929d0e0e4736","SpanId":"00f067aa0ba902b7","Path":"/orders"}
            """.ReplaceLineEndings(string.Empty));

        Assert.Equal("api-7c4f", entry.Instance);
        Assert.Equal("Orders.Api", entry.LoggerName);
        Assert.Equal(Convert.FromHexString("4bf92f3577b34da6a3ce929d0e0e4736"), entry.TraceId);
        Assert.Equal(Convert.FromHexString("00f067aa0ba902b7"), entry.SpanId);

        // Lifted rather than copied: the value is a column now, and carrying it
        // in both places would show it twice.
        Assert.Equal("""{"Path":"/orders"}""", entry.Properties);
    }

    [Fact]
    public void An_entry_promoting_nothing_is_complete()
    {
        var entry = Read(Line());

        Assert.Null(entry.Instance);
        Assert.Null(entry.LoggerName);
        Assert.Null(entry.TraceId);
        Assert.Null(entry.SpanId);
    }

    [Theory]

    // Too short, too long, not hex, and not a string at all.
    [InlineData("\"4bf92f3577b34da6\"")]
    [InlineData("\"4bf92f3577b34da6a3ce929d0e0e473600\"")]
    [InlineData("\"zzf92f3577b34da6a3ce929d0e0e4736\"")]
    [InlineData("42")]
    public void A_trace_that_is_not_sixteen_well_formed_bytes_stays_an_ordinary_property(
        string delivered)
    {
        var entry = Read(Line($""" ,"TraceId":{delivered} """));

        // Refused into the column, kept in the object. The alternative is a
        // column promising a shape it does not have.
        Assert.Null(entry.TraceId);
        Assert.Equal($$"""{"TraceId":{{delivered.Trim()}}}""", entry.Properties);
    }

    [Fact]
    public void A_promoted_property_is_still_something_a_placeholder_can_name()
    {
        // It was delivered, and that is the whole of the rule. Promotion is
        // about where the value is stored, not about whether it exists.
        var entry = Read(
            $$"""{"@t":"{{Happened}}","@mt":"Started on {instance}","instance":"api-7c4f"}""");

        Assert.Equal("Started on api-7c4f", entry.RenderedMessage);
        Assert.Equal("api-7c4f", entry.Instance);
        Assert.Null(entry.Properties);
    }

    [Fact]
    public void An_over_long_message_is_truncated_and_flagged()
    {
        var entry = Read(
            $$"""{"@t":"{{Happened}}","@mt":"{{new string('x', Caps.RenderedMessageBytes + 500)}}"}""");

        Assert.Equal(Caps.RenderedMessageBytes, entry.RenderedMessage.Length);
        Assert.True(entry.MessageTruncated);

        // The template is kept whole. The flag and the cap are about the
        // rendered form, which is what is read and what is searched.
        Assert.Equal(Caps.RenderedMessageBytes + 500, entry.MessageTemplate.Length);
    }

    [Fact]
    public void An_over_long_exception_is_truncated_and_flagged()
    {
        var entry = Read(
            $$"""
            {"@t":"{{Happened}}","@mt":"Disk full","@x":"{{new string('x', Caps.ExceptionBytes + 1)}}"}
            """);

        Assert.Equal(Caps.ExceptionBytes, entry.Exception!.Length);
        Assert.True(entry.ExceptionTruncated);
        Assert.False(entry.MessageTruncated);
    }

    [Fact]
    public void An_unknown_reserved_key_is_neither_a_field_nor_a_property()
    {
        // CLEF's own event id. It is not on the list of things that make an
        // entry invalid, and treating a format's growth as a defect in the
        // sender would be the wrong way round.
        var entry = Read($$"""{"@t":"{{Happened}}","@mt":"Hello","@i":"a1b2c3d4","Path":"/x"}""");

        Assert.Equal("""{"Path":"/x"}""", entry.Properties);
    }

    [Theory]
    [InlineData("""{"@mt":"Disk full"}""", "@t is missing")]
    [InlineData("""{"@t":42,"@mt":"Disk full"}""", "@t is missing")]
    [InlineData("""{"@t":"2026-08-07T09:15:00.417Z"}""", "@mt is missing")]
    [InlineData("""{"@t":"2026-08-07T09:15:00.417Z","@mt":42}""", "@mt is missing")]
    public void The_two_required_fields_are_required(string line, string reason) =>
        Assert.Equal(reason, ReasonFor(line));

    [Theory]

    // A local time without an offset. The two clocks of ADR 0007 are only worth
    // having apart if the sender's is a moment and not a moment somewhere.
    [InlineData("2026-08-07T09:15:00")]
    [InlineData("the seventh of August")]
    [InlineData("")]
    public void A_timestamp_without_an_offset_is_not_a_timestamp(string stamped) =>
        Assert.Equal(
            "@t is not a timestamp with an offset or Z",
            ReasonFor($$"""{"@t":"{{stamped}}","@mt":"Disk full"}"""));

    [Theory]
    [InlineData("2026-08-07T09:15:00Z")]
    [InlineData("2026-08-07T09:15:00+02:00")]
    [InlineData("2026-08-07T09:15:00-05:30")]
    public void A_timestamp_that_names_its_zone_is_read_with_it(string stamped)
    {
        var entry = Read($$"""{"@t":"{{stamped}}","@mt":"Disk full"}""");

        Assert.Equal(DateTimeOffset.Parse(stamped), entry.EventTime);
    }

    [Theory]
    [InlineData("\"Fatality\"")]
    [InlineData("\"\"")]
    [InlineData("3")]
    public void A_level_that_is_not_one_is_refused_rather_than_coerced(string named) =>
        // Never quietly Information: a wrong level is worse than a counted
        // rejection the operator can see.
        Assert.Equal("@l is not a level", ReasonFor(Line($""" ,"@l":{named} """)));

    [Fact]
    public void A_pre_rendered_message_is_refused_rather_than_ignored() =>
        // So that there stays one place where rendering happens and it is the
        // server (ADR 0004).
        Assert.Equal(
            "@m is present; the server renders @mt",
            ReasonFor(Line(""" ,"@m":"Disk full on /dev/sda1" """)));

    [Theory]
    [InlineData("[1,2,3]")]
    [InlineData("\"a line\"")]
    [InlineData("42")]
    [InlineData("null")]
    public void A_line_that_is_not_an_object_is_not_an_entry(string line) =>
        Assert.Equal("the line is not a JSON object", ReasonFor(line));

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{\"@t\":")]
    [InlineData("{} {}")]
    public void A_line_that_is_not_json_is_not_an_entry(string line) =>
        Assert.Equal("the line is not JSON", ReasonFor(line));

    [Fact]
    public void More_properties_than_the_cap_allows_is_not_an_entry()
    {
        // Not truncated: dropping the sixty-fifth would be a silent
        // modification, and which one went would be arbitrary.
        var properties = string.Join(
            ',',
            Enumerable.Range(0, Caps.PropertiesPerEntry + 1).Select(index => $"\"p{index}\":{index}"));

        Assert.Equal(
            $"more than {Caps.PropertiesPerEntry} properties", ReasonFor(Line($" ,{properties} ")));
    }

    [Fact]
    public void Exactly_as_many_properties_as_the_cap_allows_is_an_entry()
    {
        var properties = string.Join(
            ',',
            Enumerable.Range(0, Caps.PropertiesPerEntry).Select(index => $"\"p{index}\":{index}"));

        Assert.Equal(Caps.PropertiesPerEntry, Delivered(Read(Line($" ,{properties} "))));
    }

    [Theory]
    [InlineData("Order", """{"Line":{"Sku":"x"}}""")]
    [InlineData("Orders", """[{"Lines":[1]}]""")]
    public void A_property_nested_deeper_than_one_level_is_not_an_entry(string name, string value) =>
        // Nothing in this product reads inside a property (ADR 0010), so the
        // depth buys nothing and an unbounded one is a parser handed arbitrary
        // nesting by an untrusted line.
        Assert.Equal(
            $"the property {name} is nested more than {Caps.PropertyNesting} level deep",
            ReasonFor(Line($""" ,"{name}":{value} """)));

    [Theory]
    [InlineData("Order", """{"Sku":"x","Count":2}""")]
    [InlineData("Skus", """["a","b"]""")]
    public void One_level_of_nesting_is_within_the_cap(string name, string value) =>
        Assert.Equal(
            $$"""{"{{name}}":{{value}}}""", Read(Line($""" ,"{name}":{value} """)).Properties);

    private static ClefLine Read(string line)
    {
        Assert.True(
            ClefLine.TryRead(Encoding.UTF8.GetBytes(line), out var entry, out var reason), reason);

        return entry;
    }

    private static string ReasonFor(string line)
    {
        Assert.False(ClefLine.TryRead(Encoding.UTF8.GetBytes(line), out _, out var reason));

        return reason;
    }

    /// <summary>
    /// The smallest valid line, with <paramref name="more"/> spliced in before
    /// the closing brace.
    /// </summary>
    private static string Line(string more = "") =>
        $$"""{"@t":"{{Happened}}","@mt":"Disk full"{{more.Trim()}}}""";

    private static int Delivered(ClefLine entry) =>
        JsonDocument.Parse(entry.Properties!).RootElement.EnumerateObject().Count();
}
