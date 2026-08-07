using Logaffe.Domain.Tokens;

namespace Logaffe.UnitTests.Domain;

public sealed class TokenIdentifierTests
{
    [Fact]
    public void A_minted_identifier_is_the_length_the_column_holds() =>
        Assert.Equal(TokenIdentifier.Length, TokenIdentifier.Mint().Value.Length);

    [Fact]
    public void An_identifier_is_drawn_from_the_alphabet() =>
        Assert.True(TokenAlphabet.Covers(TokenIdentifier.Mint().Value));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("aaaaaaaaaaa")]
    [InlineData("aaaaaaaaaaaaa")]
    [InlineData("aaaaaaaaaaal")]
    [InlineData("AAAAAAAAAAAA")]
    [InlineData("aaaaaaaaaaa_")]
    public void A_value_of_the_wrong_shape_is_not_an_identifier(string? value)
    {
        Assert.False(TokenIdentifier.TryCreate(value, out _));
        Assert.Throws<ArgumentException>(() => TokenIdentifier.Create(value));
    }

    [Fact]
    public void Two_identifiers_of_one_value_are_the_same_identifier() =>
        // It names a row and admits nothing, so an ordinary equality is what it
        // wants — unlike the secret, which is compared in constant time.
        Assert.Equal(TokenIdentifier.Create("abcdefghijkm"), TokenIdentifier.Create("abcdefghijkm"));

    [Fact]
    public void The_alphabet_leaves_out_what_a_reader_confuses()
    {
        Assert.Equal(32, TokenAlphabet.Symbols.Length);
        Assert.Equal(TokenAlphabet.Symbols.Length, TokenAlphabet.Symbols.Distinct().Count());

        foreach (var confusable in "lo01_")
        {
            Assert.DoesNotContain(confusable, TokenAlphabet.Symbols);
        }
    }
}
