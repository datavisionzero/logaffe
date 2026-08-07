using Logaffe.Application.Operations;
using Logaffe.Domain.Tokens;

namespace Logaffe.UnitTests.Application;

/// <summary>
/// What the operator does to the credentials their agents read with: issue one
/// under a name, rename it, read it back, retire it, and look at the list that
/// says which one has gone quiet.
/// </summary>
public sealed class AgentTokenActsTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 7, 9, 0, 0, TimeSpan.Zero);

    private readonly InMemoryTokens _tokens = new();
    private readonly ReversingCipher _cipher = new();
    private readonly StoppedClock _clock = new(Now);

    [Fact]
    public async Task An_issued_token_is_an_agent_token_under_the_name_it_was_given()
    {
        var issued = await IssueAsync("terminal agent");

        Assert.Equal(TokenKind.Agent, issued.Token.Kind);
        Assert.StartsWith(TokenText.AgentPrefix, issued.Token.Text, StringComparison.Ordinal);

        var stored = Assert.Single(_tokens.StoredAgentTokens);
        Assert.Equal(issued.Id, stored.Id);
        Assert.Equal("terminal agent", stored.Name);
        Assert.Equal(issued.Token.Identifier, stored.Identifier);
        Assert.Equal(issued.Token.Secret, _cipher.Decrypt(stored.EncryptedSecret));
        Assert.Null(stored.LastUsedAt);
    }

    [Fact]
    public async Task A_name_that_is_not_a_name_is_refused()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => Issuing().ExecuteAsync("   ", TestContext.Current.CancellationToken));

        Assert.Empty(_tokens.StoredAgentTokens);
    }

    [Fact]
    public async Task Several_exist_at_once_and_none_of_them_is_the_others()
    {
        // A terminal agent and a desktop agent, so that one can be retired
        // without disturbing the other (ADR 0021). There is no maximum here.
        var first = await IssueAsync("terminal agent");
        _clock.Now = Now.AddDays(1);
        var second = await IssueAsync("desktop agent");
        _clock.Now = Now.AddDays(2);
        var third = await IssueAsync("desktop agent");

        Assert.Equal(3, _tokens.StoredAgentTokens.Count);
        Assert.Equal(
            3,
            new[] { first, second, third }
                .Select(issued => issued.Token.Identifier)
                .Distinct()
                .Count());

        // Two agents that call themselves the same thing is their business: the
        // name is a label and the identifier is what tells the rows apart.
        Assert.Equal(
            ["terminal agent", "desktop agent", "desktop agent"],
            _tokens.StoredAgentTokens.Select(token => token.Name));
    }

    [Fact]
    public async Task A_mislaid_token_is_read_back_rather_than_reissued()
    {
        var issued = await IssueAsync("terminal agent");

        var readBack = await ReadingBack().AgentTokenAsync(
            issued.Id, TestContext.Current.CancellationToken);

        Assert.NotNull(readBack);
        Assert.Equal(issued.Token.Text, readBack.Text);
    }

    [Fact]
    public async Task Renaming_changes_the_label_and_nothing_the_agent_holds()
    {
        // The name does not identify the token to the server, so an agent whose
        // token is renamed does not notice and nothing is reconnected.
        var issued = await IssueAsync("claude-code");

        Assert.True(await Renaming().ExecuteAsync(
            issued.Id, "  laptop  ", TestContext.Current.CancellationToken));

        var stored = Assert.Single(_tokens.StoredAgentTokens);
        Assert.Equal("laptop", stored.Name);

        var readBack = await ReadingBack().AgentTokenAsync(
            issued.Id, TestContext.Current.CancellationToken);
        Assert.Equal(issued.Token.Text, readBack!.Text);
    }

    [Fact]
    public async Task Renaming_a_token_that_is_not_there_says_so()
    {
        Assert.False(await Renaming().ExecuteAsync(
            Guid.CreateVersion7(), "laptop", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Revoking_one_removes_its_row_and_leaves_the_others()
    {
        var kept = await IssueAsync("terminal agent");
        var retired = await IssueAsync("the laptop that was replaced");

        Assert.True(await Revoking().AgentTokenAsync(
            retired.Id, TestContext.Current.CancellationToken));

        var left = Assert.Single(_tokens.StoredAgentTokens);
        Assert.Equal(kept.Id, left.Id);
        Assert.Null(await ReadingBack().AgentTokenAsync(
            retired.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Revoking_a_token_that_is_already_gone_says_so()
    {
        Assert.False(await Revoking().AgentTokenAsync(
            Guid.CreateVersion7(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_list_is_the_name_the_issue_date_and_the_last_use()
    {
        // The three fields that make the list worth having: a token that has
        // not been used in months is one to revoke, and this is the only place
        // that fact is visible.
        var quiet = await IssueAsync("the laptop that was replaced");
        _clock.Now = Now.AddDays(1);
        var busy = await IssueAsync("terminal agent");
        _tokens.StoredAgentTokens[1].WasUsedAt(Now.AddDays(30));

        var listed = await Listing().ExecuteAsync(TestContext.Current.CancellationToken);

        Assert.Equal([quiet.Id, busy.Id], listed.Select(token => token.Id));
        Assert.Equal("the laptop that was replaced", listed[0].Name);
        Assert.Equal(Now, listed[0].IssuedAt);
        Assert.Null(listed[0].LastUsedAt);
        Assert.Equal(Now.AddDays(30), listed[1].LastUsedAt);
    }

    [Fact]
    public async Task An_agent_token_is_not_reachable_where_an_ingest_token_is_meant()
    {
        var issued = await IssueAsync("terminal agent");

        Assert.Null(await ReadingBack().IngestTokenAsync(
            issued.Id, TestContext.Current.CancellationToken));
        Assert.False(await Revoking().IngestTokenAsync(
            issued.Id, TestContext.Current.CancellationToken));
        Assert.Single(_tokens.StoredAgentTokens);
    }

    private Task<IssuedToken> IssueAsync(string name) =>
        Issuing().ExecuteAsync(name, TestContext.Current.CancellationToken);

    private IssueAgentToken Issuing() => new(_tokens, _cipher, _clock);

    private ListAgentTokens Listing() => new(_tokens);

    private ReadTokenBack ReadingBack() => new(_tokens, _cipher);

    private RenameAgentToken Renaming() => new(_tokens);

    private RevokeToken Revoking() => new(_tokens);
}
