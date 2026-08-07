using Logaffe.Domain.Tokens;

namespace Logaffe.UnitTests.Domain;

public sealed class AgentTokenTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 6, 12, 0, 0, TimeSpan.Zero);

    private static readonly byte[] Ciphertext = [1, 2, 3, 4];

    [Fact]
    public void An_issued_token_is_named_and_has_never_been_used()
    {
        var identifier = TokenIdentifier.Mint();

        var token = AgentToken.Issue("terminal agent", identifier, Ciphertext, Now);

        Assert.NotEqual(Guid.Empty, token.Id);
        Assert.Equal("terminal agent", token.Name);
        Assert.Equal(identifier, token.Identifier);
        Assert.Equal(Ciphertext, token.EncryptedSecret);
        Assert.Equal(Now, token.IssuedAt);
        Assert.Null(token.LastUsedAt);
    }

    [Fact]
    public void Renaming_changes_nothing_else()
    {
        var token = AgentToken.Issue("terminal agent", TokenIdentifier.Mint(), Ciphertext, Now);
        var identity = token.Id;
        var identifier = token.Identifier;

        token.Rename("desktop agent");

        // The name is a label for the operator's list; it does not identify the
        // token to the server, which is what the identifier is for.
        Assert.Equal("desktop agent", token.Name);
        Assert.Equal(identity, token.Id);
        Assert.Equal(identifier, token.Identifier);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void An_agent_token_has_a_name(string name) =>
        Assert.Throws<ArgumentException>(
            () => AgentToken.Issue(name, TokenIdentifier.Mint(), Ciphertext, Now));

    [Fact]
    public void A_name_that_will_not_fit_the_column_is_refused_here() =>
        Assert.Throws<ArgumentException>(() => AgentToken.Issue(
            new string('x', AgentToken.NameMaxLength + 1), TokenIdentifier.Mint(), Ciphertext, Now));

    [Fact]
    public void Two_agents_may_call_themselves_the_same_thing()
    {
        var first = AgentToken.Issue("agent", TokenIdentifier.Mint(), Ciphertext, Now);
        var second = AgentToken.Issue("agent", TokenIdentifier.Mint(), Ciphertext, Now);

        Assert.Equal(first.Name, second.Name);
        Assert.NotEqual(first.Identifier, second.Identifier);
    }

    [Fact]
    public void Using_a_token_records_when()
    {
        var token = AgentToken.Issue("agent", TokenIdentifier.Mint(), Ciphertext, Now);

        token.WasUsedAt(Now.AddDays(3));

        // The load-bearing field: a token that has not been used in months is
        // one to revoke, and this list is the only place that is visible.
        Assert.Equal(Now.AddDays(3), token.LastUsedAt);
    }
}
