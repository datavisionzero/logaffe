using Logaffe.Domain.Tokens;

namespace Logaffe.UnitTests.Domain;

public sealed class TokenTextTests
{
    [Theory]
    [InlineData(TokenKind.Ingest, "logaffe_ingest")]
    [InlineData(TokenKind.Agent, "logaffe_agent")]
    [InlineData(TokenKind.Host, "logaffe_host")]
    public void A_minted_token_is_its_prefix_its_identifier_and_its_secret(
        TokenKind kind, string prefix)
    {
        var token = TokenText.Mint(kind);

        Assert.Equal(
            $"{prefix}_{token.Identifier.Value}_{token.Secret}",
            token.Text);
        Assert.Equal(TokenIdentifier.Length, token.Identifier.Value.Length);
        Assert.Equal(TokenText.SecretLength, token.Secret.Length);
    }

    [Fact]
    public void A_minted_token_parses_back_to_itself()
    {
        var minted = TokenText.Mint(TokenKind.Ingest);

        Assert.True(TokenText.TryParse(minted.Text, out var parsed));
        Assert.Equal(TokenKind.Ingest, parsed.Kind);
        Assert.Equal(minted.Identifier, parsed.Identifier);
        Assert.True(parsed.SecretMatches(minted.Secret));
    }

    [Fact]
    public void Two_minted_tokens_share_neither_half()
    {
        var first = TokenText.Mint(TokenKind.Agent);
        var second = TokenText.Mint(TokenKind.Agent);

        Assert.NotEqual(first.Identifier, second.Identifier);
        Assert.NotEqual(first.Secret, second.Secret);
    }

    [Fact]
    public void The_prefix_says_which_endpoint_a_token_belongs_at()
    {
        // Pasting one where the other belongs is a mistake that will happen, and
        // it is answered here rather than three layers in (ADR 0021).
        Assert.True(TokenText.TryParse(TokenText.Mint(TokenKind.Agent).Text, out var agent));

        Assert.Equal(TokenKind.Agent, agent.Kind);
        Assert.NotEqual(TokenKind.Ingest, agent.Kind);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-token")]
    [InlineData("logaffe_admin_aaaaaaaaaaaa_bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb")]
    [InlineData("logaffe_ingest_aaaaaaaaaaaa")]
    [InlineData("logaffe_ingest_aaaaaaaaaaaa_bbbbbbbbbbbb_cccccccccccc")]
    public void A_value_that_is_not_one_of_these_is_refused(string? value) =>
        Assert.False(TokenText.TryParse(value, out _));

    [Fact]
    public void An_identifier_of_the_wrong_length_is_refused()
    {
        var token = TokenText.Mint(TokenKind.Ingest);
        var short_identifier = token.Identifier.Value[1..];

        Assert.False(TokenText.TryParse(
            $"{TokenText.IngestPrefix}_{short_identifier}_{token.Secret}", out _));
    }

    [Fact]
    public void A_secret_of_the_wrong_length_is_refused()
    {
        var token = TokenText.Mint(TokenKind.Ingest);

        Assert.False(TokenText.TryParse(
            $"{TokenText.IngestPrefix}_{token.Identifier.Value}_{token.Secret[1..]}", out _));
    }

    [Fact]
    public void A_character_outside_the_alphabet_is_refused()
    {
        var token = TokenText.Mint(TokenKind.Ingest);
        // `l` and `o` are absent from the alphabet because a person copying a
        // token by hand confuses them with `1` and `0`.
        var secret = string.Concat("l", token.Secret[1..]);

        Assert.False(TokenText.TryParse(
            $"{TokenText.IngestPrefix}_{token.Identifier.Value}_{secret}", out _));
    }

    [Fact]
    public void A_secret_that_is_not_the_stored_one_does_not_match()
    {
        var token = TokenText.Mint(TokenKind.Ingest);
        var other = TokenText.Mint(TokenKind.Ingest);

        Assert.True(token.SecretMatches(token.Secret));
        Assert.False(token.SecretMatches(other.Secret));
        Assert.False(token.SecretMatches(string.Empty));
    }

    [Fact]
    public void A_token_read_back_is_the_token_that_was_issued()
    {
        var issued = TokenText.Mint(TokenKind.Agent);

        // What the operator reads back: the identifier from the row, the secret
        // decrypted, and the two put together again (ADR 0022).
        var readBack = TokenText.From(TokenKind.Agent, issued.Identifier, issued.Secret);

        Assert.Equal(issued.Text, readBack.Text);
    }

    [Fact]
    public void Reassembling_with_a_secret_of_the_wrong_shape_is_refused() =>
        Assert.Throws<ArgumentException>(
            () => TokenText.From(TokenKind.Agent, TokenIdentifier.Mint(), "too-short"));

    [Fact]
    public void An_accidental_interpolation_does_not_carry_the_secret()
    {
        var token = TokenText.Mint(TokenKind.Ingest);

        var interpolated = $"{token}";

        // Applications log the configuration they started with, and a token that
        // ends up in a log entry ends up in this product.
        Assert.DoesNotContain(token.Secret, interpolated, StringComparison.Ordinal);
        Assert.Contains(token.Identifier.Value, interpolated, StringComparison.Ordinal);
    }
}
