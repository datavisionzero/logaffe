using System.Text;
using Logaffe.Domain.Entries;

namespace Logaffe.UnitTests.Domain;

/// <summary>
/// The one modification logaffe makes to what it was given.
/// </summary>
/// <remarks>
/// The entries that overrun a cap are the four-megabyte stack traces and the
/// dumped payloads, which is to say the entries an operator is most likely to
/// have gone looking for — so they are cut and flagged rather than refused, and
/// the flag is the whole of what keeps a shortened stack trace from reading as a
/// complete one (ADR 0008).
/// </remarks>
public sealed class CapsTests
{
    [Fact]
    public void Text_within_its_cap_is_the_text_that_arrived()
    {
        var (text, truncated) = Caps.CutTo("System.IO.IOException: No space left", Caps.ExceptionBytes);

        Assert.Equal("System.IO.IOException: No space left", text);
        Assert.False(truncated);
    }

    [Fact]
    public void Text_exactly_at_its_cap_is_not_cut()
    {
        // The boundary is inclusive: a cap is what is allowed, not what is one
        // short of allowed.
        var (text, truncated) = Caps.CutTo(new string('x', 32), 32);

        Assert.Equal(32, text!.Length);
        Assert.False(truncated);
    }

    [Fact]
    public void Text_over_its_cap_is_cut_to_it_and_flagged()
    {
        var (text, truncated) = Caps.CutTo(new string('x', 100), 32);

        Assert.Equal(new string('x', 32), text);
        Assert.True(truncated);
    }

    [Fact]
    public void Nothing_is_still_nothing()
    {
        // An entry with no exception is the ordinary entry, and it is not a
        // truncated one.
        var (text, truncated) = Caps.CutTo(null, Caps.ExceptionBytes);

        Assert.Null(text);
        Assert.False(truncated);
    }

    [Fact]
    public void The_cap_counts_bytes_of_utf8_and_not_characters()
    {
        // Four bytes each, so eight of them are thirty-two bytes and the ninth
        // does not fit — even though thirty-two characters would have.
        var (text, truncated) = Caps.CutTo(string.Concat(Enumerable.Repeat("😀", 16)), 32);

        Assert.Equal(32, Encoding.UTF8.GetByteCount(text!));
        Assert.Equal(8, text!.EnumerateRunes().Count());
        Assert.True(truncated);
    }

    [Fact]
    public void The_cut_lands_on_a_character_and_never_inside_one()
    {
        // Thirty of the cap's thirty-two bytes fit seven of these; the eighth
        // would need four and gets two. Cutting there would leave half a
        // surrogate pair, and what came out would not be text any more.
        var (text, _) = Caps.CutTo(string.Concat(Enumerable.Repeat("😀", 16)), 30);

        Assert.Equal(7, text!.EnumerateRunes().Count());
        Assert.Equal(28, Encoding.UTF8.GetByteCount(text));

        // Every rune whole, which is the same thing said from the other end: a
        // cut inside a pair would leave one that is not.
        Assert.All(text.EnumerateRunes(), rune => Assert.Equal("😀", rune.ToString()));
    }
}
