using Logaffe.Domain.Entries;

namespace Logaffe.UnitTests.Domain;

public sealed class LevelTests
{
    [Fact]
    public void An_entry_that_names_no_level_is_information() =>
        Assert.Equal(Level.Information, Levels.WhenAbsent);

    [Theory]
    [InlineData("Verbose", Level.Verbose)]
    [InlineData("Debug", Level.Debug)]
    [InlineData("Information", Level.Information)]
    [InlineData("Warning", Level.Warning)]
    [InlineData("Error", Level.Error)]
    [InlineData("Fatal", Level.Fatal)]
    public void Serilogs_six_are_read(string name, Level expected)
    {
        Assert.True(Levels.TryParse(name, out var level));
        Assert.Equal(expected, level);
    }

    [Theory]
    [InlineData("Trace", Level.Verbose)]
    [InlineData("Critical", Level.Fatal)]
    public void The_two_names_extensions_logging_spells_differently_are_read_too(
        string name, Level expected)
    {
        Assert.True(Levels.TryParse(name, out var level));
        Assert.Equal(expected, level);
    }

    [Theory]
    [InlineData("WARNING")]
    [InlineData("warning")]
    [InlineData("  Warning ")]
    public void Matching_is_case_insensitive(string name)
    {
        Assert.True(Levels.TryParse(name, out var level));
        Assert.Equal(Level.Warning, level);
    }

    [Theory]
    [InlineData("Severe")]
    [InlineData("")]
    [InlineData(null)]
    public void An_unrecognized_level_is_refused_rather_than_coerced(string? name) =>
        // A wrong level is worse than a counted rejection the operator can see,
        // so nothing falls back to Information here.
        Assert.False(Levels.TryParse(name, out _));

    [Fact]
    public void Warning_and_above_is_three_and_above()
    {
        // docs/storage.md indexes exactly this predicate. If these numbers move,
        // the partial index stops meaning what it says.
        Assert.Equal(3, (int)Level.Warning);
        Assert.True(Level.Error > Level.Warning);
        Assert.True(Level.Fatal > Level.Error);
        Assert.True(Level.Information < Level.Warning);
    }
}
