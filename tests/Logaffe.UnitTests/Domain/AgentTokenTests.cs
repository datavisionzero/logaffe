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

        var token = Issue("terminal agent", identifier: identifier);

        Assert.NotEqual(Guid.Empty, token.Id);
        Assert.Equal("terminal agent", token.Name);
        Assert.Equal(identifier, token.Identifier);
        Assert.Equal(Ciphertext, token.EncryptedSecret);
        Assert.Equal(Now, token.IssuedAt);
        Assert.Null(token.LastUsedAt);
    }

    [Fact]
    public void What_a_token_may_do_is_settled_when_it_is_issued()
    {
        var reading = Issue("terminal agent");
        var administering = Issue("the setting-up agent", AgentTokenKind.Administering);
        var destroying = Issue("the tidying agent", AgentTokenKind.Administering, mayDestroy: true);

        Assert.Equal(AgentTokenKind.Reading, reading.Kind);
        Assert.False(reading.MayDestroy);

        // Off unless it was asked for, which is the half of ADR 0046 that is
        // about the four acts data does not come back from.
        Assert.Equal(AgentTokenKind.Administering, administering.Kind);
        Assert.False(administering.MayDestroy);

        Assert.True(destroying.MayDestroy);
    }

    [Fact]
    public void Only_an_administering_token_can_be_issued_to_destroy() =>
        // Not a smaller request than an administering one — a nonsense one. A
        // reading token makes no change of any kind, so there is nothing for the
        // flag to be about.
        Assert.Throws<ArgumentException>(
            () => Issue("terminal agent", AgentTokenKind.Reading, mayDestroy: true));

    [Fact]
    public void Renaming_changes_nothing_else()
    {
        var token = Issue("terminal agent", AgentTokenKind.Administering, mayDestroy: true);
        var identity = token.Id;
        var identifier = token.Identifier;

        token.Rename("desktop agent");

        // The name is a label for the operator's list; it does not identify the
        // token to the server, which is what the identifier is for.
        Assert.Equal("desktop agent", token.Name);
        Assert.Equal(identity, token.Id);
        Assert.Equal(identifier, token.Identifier);

        // Renaming is the only act there is on a token, and what a token may do
        // is deliberately not among the things it touches: changing that means
        // issuing another and revoking this one (ADR 0046).
        Assert.Equal(AgentTokenKind.Administering, token.Kind);
        Assert.True(token.MayDestroy);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void An_agent_token_has_a_name(string name) =>
        Assert.Throws<ArgumentException>(() => Issue(name));

    [Fact]
    public void A_name_that_will_not_fit_the_column_is_refused_here() =>
        Assert.Throws<ArgumentException>(
            () => Issue(new string('x', AgentToken.NameMaxLength + 1)));

    [Fact]
    public void Two_agents_may_call_themselves_the_same_thing()
    {
        var first = Issue("agent");
        var second = Issue("agent");

        Assert.Equal(first.Name, second.Name);
        Assert.NotEqual(first.Identifier, second.Identifier);
    }

    [Fact]
    public void Using_a_token_records_when()
    {
        var token = Issue("agent");

        token.WasUsedAt(Now.AddDays(3));

        // The load-bearing field: a token that has not been used in months is
        // one to revoke, and this list is the only place that is visible.
        Assert.Equal(Now.AddDays(3), token.LastUsedAt);
    }

    private static AgentToken Issue(
        string name,
        AgentTokenKind kind = AgentTokenKind.Reading,
        bool mayDestroy = false,
        TokenIdentifier? identifier = null) =>
        AgentToken.Issue(
            name, kind, mayDestroy, identifier ?? TokenIdentifier.Mint(), Ciphertext, Now);
}
