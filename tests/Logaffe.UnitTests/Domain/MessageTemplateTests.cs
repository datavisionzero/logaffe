using Logaffe.Domain.Entries;

namespace Logaffe.UnitTests.Domain;

/// <summary>
/// The rendering rule, which is narrower than Serilog's on purpose.
/// </summary>
/// <remarks>
/// Most of what is asked below is what the rule does <b>not</b> do. Log content
/// is untrusted and routinely contains text the application never wrote, so the
/// property worth holding on to is that a line nobody delivered properties for
/// comes out exactly as it went in — braces and all (ADR 0004).
/// </remarks>
public sealed class MessageTemplateTests
{
    [Fact]
    public void A_plain_line_renders_to_itself()
    {
        // The `curl` case of docs/ingestion.md: one field, no syntax, and no
        // question of what a template without holes means.
        const string plain = "Disk full on /dev/sda1";

        Assert.Equal(plain, MessageTemplate.Render(plain, Delivered(("Ip", "203.0.113.7"))));
    }

    [Fact]
    public void A_placeholder_whose_property_was_delivered_is_substituted()
    {
        var rendered = MessageTemplate.Render(
            "User {UserId} failed login from {Ip}",
            Delivered(("UserId", "42"), ("Ip", "203.0.113.7")));

        Assert.Equal("User 42 failed login from 203.0.113.7", rendered);
    }

    [Fact]
    public void A_placeholder_whose_property_was_not_delivered_stays_as_it_arrived()
    {
        // Serilog would leave this standing too. What matters is that it is the
        // same rule as every other unmatched brace below rather than a special
        // case.
        var rendered = MessageTemplate.Render(
            "User {UserId} failed login from {Ip}", Delivered(("UserId", "42")));

        Assert.Equal("User 42 failed login from {Ip}", rendered);
    }

    [Theory]

    // The doubled brace Serilog reads as an escape. logaffe does not: an
    // application logging a raw request body will sooner or later log these, and
    // the escaping rule would rewrite text it never wrote.
    [InlineData("{{Ip}}")]
    [InlineData("{{ \"Ip\": \"203.0.113.7\" }}")]

    // A brace with nothing closing it, and one closing nothing.
    [InlineData("Parsing {Ip failed")]
    [InlineData("Parsing Ip} failed")]

    // A name that is not one, and a hole with no name in it at all.
    [InlineData("Rejected {not a property} outright")]
    [InlineData("Rejected {} outright")]
    public void Every_other_brace_stays_character_for_character(string template) =>
        Assert.Equal(template, MessageTemplate.Render(template, Delivered(("Ip", "203.0.113.7"))));

    [Fact]
    public void A_delivery_with_no_properties_renders_the_template_it_arrived_as()
    {
        const string template = "User {UserId} failed login";

        Assert.Equal(template, MessageTemplate.Render(template, Delivered()));
    }

    [Fact]
    public void A_destructuring_hint_says_how_the_value_was_captured_and_not_what_it_is_called()
    {
        // Serilog writes `{@User}` in the template and `User` as the CLEF key,
        // so the sigil is read past to find the property.
        var rendered = MessageTemplate.Render(
            "Signed in {@User} from {$Ip}",
            Delivered(("User", """{"Id":42}"""), ("Ip", "203.0.113.7")));

        Assert.Equal("""Signed in {"Id":42} from 203.0.113.7""", rendered);
    }

    [Fact]
    public void A_format_specifier_finds_the_name_and_is_then_dropped()
    {
        // The value is stored as it was delivered and logaffe holds no type to
        // apply a specifier to. Leaving the whole placeholder standing instead
        // would put `{Elapsed:0.000}` on the operator's screen.
        var rendered = MessageTemplate.Render(
            "Handled in {Elapsed:0.000} ms at {Rate,10}",
            Delivered(("Elapsed", "3.7"), ("Rate", "12")));

        Assert.Equal("Handled in 3.7 ms at 12", rendered);
    }

    [Fact]
    public void A_property_value_that_is_itself_a_template_is_not_rendered_again()
    {
        // One pass, and it never reopens what it has substituted. A value that
        // names another property is content, and a second pass over it is
        // exactly the rewriting this rule exists to avoid.
        var rendered = MessageTemplate.Render(
            "Received {Body}", Delivered(("Body", "{Secret}"), ("Secret", "s3cret")));

        Assert.Equal("Received {Secret}", rendered);
    }

    [Fact]
    public void A_name_is_matched_exactly()
    {
        var rendered = MessageTemplate.Render(
            "From {ip} and {Ip}", Delivered(("Ip", "203.0.113.7")));

        Assert.Equal("From {ip} and 203.0.113.7", rendered);
    }

    private static Dictionary<string, string> Delivered(params (string Name, string Value)[] properties) =>
        properties.ToDictionary(
            property => property.Name, property => property.Value, StringComparer.Ordinal);
}
