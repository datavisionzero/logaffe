using Logaffe.Domain.Queries;

namespace Logaffe.UnitTests.Domain;

public sealed class SearchTextTests
{
    [Theory]
    [InlineData("a")]
    [InlineData("ab")]
    [InlineData("")]
    [InlineData(null)]
    public void Shorter_than_three_characters_is_refused_rather_than_run(string? value) =>
        // Below three the trigram index cannot be used at all, and the search
        // scans the project — 75 seconds over ten million entries.
        Assert.False(SearchText.TryCreate(value, out _));

    [Fact]
    public void Three_characters_is_enough()
    {
        Assert.True(SearchText.TryCreate("abc", out var text));
        Assert.Equal("abc", text.Value);
    }

    [Fact]
    public void The_length_is_measured_after_trimming()
    {
        Assert.False(SearchText.TryCreate("  ab  ", out _));
        Assert.True(SearchText.TryCreate("  abc  ", out var text));
        Assert.Equal("abc", text.Value);
    }

    [Fact]
    public void Create_refuses_loudly() =>
        Assert.Throws<ArgumentException>(() => SearchText.Create("ab"));
}
