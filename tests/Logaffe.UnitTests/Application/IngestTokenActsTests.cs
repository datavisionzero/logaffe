using System.Text;
using Logaffe.Application.Operations;
using Logaffe.Domain.Projects;
using Logaffe.Domain.Tokens;

namespace Logaffe.UnitTests.Application;

/// <summary>
/// What the operator does to a project's token: issue it, issue the second one
/// rotation is made of, read either back, and revoke the one that has gone
/// quiet.
/// </summary>
public sealed class IngestTokenActsTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 7, 9, 0, 0, TimeSpan.Zero);

    private readonly InMemoryProjects _projects = new();
    private readonly InMemoryTokens _tokens = new();
    private readonly ReversingCipher _cipher = new();
    private readonly StoppedClock _clock = new(Now);

    /// <summary>
    /// The project these acts are reached through. It exists, because issuing
    /// into one that does not is its own answer.
    /// </summary>
    private readonly Guid _project;

    public IngestTokenActsTests() =>
        _project = _projects.Holding("api", RetentionWindow.OfDays(7), Now).Id;

    [Fact]
    public async Task An_issued_token_is_an_ingest_token_for_the_project_that_asked()
    {
        var issued = await IssueAsync();

        Assert.NotNull(issued);
        Assert.Equal(TokenKind.Ingest, issued.Token.Kind);
        Assert.StartsWith(TokenText.IngestPrefix, issued.Token.Text, StringComparison.Ordinal);
        Assert.Equal(Now, issued.IssuedAt);

        var stored = Assert.Single(_tokens.Stored);
        Assert.Equal(issued.Id, stored.Id);
        Assert.Equal(_project, stored.ProjectId);
        Assert.Equal(issued.Token.Identifier, stored.Identifier);
        Assert.Null(stored.LastUsedAt);
    }

    [Fact]
    public async Task The_row_holds_the_secret_sealed_and_the_identifier_in_the_clear()
    {
        // ADR 0022: what a stolen database backup yields is this row, and the
        // key that opens it is on the host volume rather than beside it.
        var issued = await IssueAsync();

        var stored = Assert.Single(_tokens.Stored);
        Assert.NotEqual(
            Encoding.UTF8.GetBytes(issued!.Token.Secret), stored.EncryptedSecret);
        Assert.Equal(issued.Token.Secret, _cipher.Decrypt(stored.EncryptedSecret));
        Assert.Equal(issued.Token.Identifier, stored.Identifier);
    }

    [Fact]
    public async Task A_mislaid_token_is_read_back_rather_than_reissued()
    {
        // The whole of ADR 0022, and the first place the cipher is used for
        // something other than a check.
        var issued = await IssueAsync();

        var readBack = await ReadingBack().IngestTokenAsync(
            issued!.Id, TestContext.Current.CancellationToken);

        Assert.NotNull(readBack);
        Assert.Equal(issued.Token.Text, readBack.Text);
        Assert.Single(_tokens.Stored);
    }

    [Fact]
    public async Task Reading_back_a_token_that_is_not_there_finds_nothing()
    {
        Assert.Null(await ReadingBack().IngestTokenAsync(
            Guid.CreateVersion7(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Two_tokens_exist_while_the_project_is_being_rotated()
    {
        var first = await IssueAsync();
        _clock.Now = Now.AddDays(30);
        var second = await IssueAsync();

        Assert.NotNull(second);
        Assert.NotEqual(first!.Token.Identifier, second.Token.Identifier);
        Assert.NotEqual(first.Token.Secret, second.Token.Secret);

        // Oldest first, so the one being rotated away is the one at the top.
        var listed = await ListedAsync(_project);
        Assert.Equal([first.Id, second.Id], listed.Select(token => token.Id));
    }

    [Fact]
    public async Task A_third_token_is_refused_and_nothing_is_written()
    {
        await IssueAsync();
        await IssueAsync();
        var writesBefore = _tokens.Writes;

        // The rotation model saying what it is for: the operator revokes the
        // one they are retiring rather than collecting a third.
        var third = await Issuing().ExecuteAsync(_project, TestContext.Current.CancellationToken);

        Assert.Equal(IssueOutcome.AlreadyHoldsTwo, third.Outcome);
        Assert.Null(third.Token);
        Assert.Equal(IngestToken.MaximumPerProject, _tokens.Stored.Count);
        Assert.Equal(writesBefore, _tokens.Writes);
    }

    [Fact]
    public async Task A_token_is_not_issued_into_a_project_that_is_not_there()
    {
        // The foreign key would refuse it as a failure of the installation;
        // what happened is that the operator named something that is gone.
        var attempt = await Issuing().ExecuteAsync(
            Guid.CreateVersion7(), TestContext.Current.CancellationToken);

        Assert.Equal(IssueOutcome.NoSuchProject, attempt.Outcome);
        Assert.Null(attempt.Token);
        Assert.Empty(_tokens.Stored);
        Assert.Equal(0, _tokens.Writes);
    }

    [Fact]
    public async Task The_tokens_of_a_project_that_is_not_there_are_not_an_empty_list()
    {
        // A closed door and a deleted project are two different readings, and
        // an empty list for both is the settings of something gone.
        Assert.Null(await Listing().ExecuteAsync(
            Guid.CreateVersion7(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Another_projects_tokens_are_neither_counted_nor_listed()
    {
        var other = _projects.Holding("web", RetentionWindow.OfDays(7), Now).Id;
        await IssueAsync();
        await IssueAsync();

        var elsewhere = await Issuing().ExecuteAsync(other, TestContext.Current.CancellationToken);

        Assert.Equal(IssueOutcome.Issued, elsewhere.Outcome);
        Assert.Equal(
            [elsewhere.Token!.Id],
            (await ListedAsync(other)).Select(token => token.Id));
    }

    [Fact]
    public async Task Revoking_removes_the_row_and_leaves_room_for_another()
    {
        var first = await IssueAsync();
        var second = await IssueAsync();

        Assert.True(await Revoking().IngestTokenAsync(
            first!.Id, TestContext.Current.CancellationToken));

        // Removed rather than marked: nothing of the revoked token is left, not
        // its identifier and not its sealed secret.
        var left = Assert.Single(_tokens.Stored);
        Assert.Equal(second!.Id, left.Id);
        Assert.Null(await ReadingBack().IngestTokenAsync(
            first.Id, TestContext.Current.CancellationToken));

        Assert.NotNull(await IssueAsync());
    }

    [Fact]
    public async Task Revoking_a_token_that_is_already_gone_says_so_and_writes_nothing()
    {
        var issued = await IssueAsync();
        Assert.True(await Revoking().IngestTokenAsync(
            issued!.Id, TestContext.Current.CancellationToken));
        var writesBefore = _tokens.Writes;

        // A second click, or a token revoked in another tab, and not a failure
        // of anything.
        Assert.False(await Revoking().IngestTokenAsync(
            issued.Id, TestContext.Current.CancellationToken));
        Assert.Equal(writesBefore, _tokens.Writes);
    }

    [Fact]
    public async Task A_project_may_be_left_receiving_nothing()
    {
        var issued = await IssueAsync();

        Assert.True(await Revoking().IngestTokenAsync(
            issued!.Id, TestContext.Current.CancellationToken));

        Assert.Empty(await ListedAsync(_project));
    }

    [Fact]
    public async Task The_list_carries_what_rotation_is_read_from_and_no_secret()
    {
        var issued = await IssueAsync();
        _tokens.Stored[0].WasUsedAt(Now.AddHours(3));

        var listed = Assert.Single(await ListedAsync(_project));

        Assert.Equal(issued!.Id, listed.Id);
        Assert.Equal(issued.Token.Identifier, listed.Identifier);
        Assert.Equal(Now, listed.IssuedAt);
        Assert.Equal(Now.AddHours(3), listed.LastUsedAt);
    }

    [Fact]
    public async Task An_ingest_token_is_not_reachable_where_an_agent_token_is_meant()
    {
        // Two tables and two acts, and an identity out of one of them names
        // nothing in the other.
        var issued = await IssueAsync();

        Assert.Null(await ReadingBack().AgentTokenAsync(
            issued!.Id, TestContext.Current.CancellationToken));
        Assert.False(await Revoking().AgentTokenAsync(
            issued.Id, TestContext.Current.CancellationToken));
        Assert.Single(_tokens.Stored);
    }

    /// <summary>
    /// One issuing into the project that is there, which is every case but the
    /// two that are about the project rather than the token.
    /// </summary>
    private async Task<IssuedToken?> IssueAsync() =>
        (await Issuing().ExecuteAsync(_project, TestContext.Current.CancellationToken)).Token;

    private async Task<IReadOnlyList<ListedIngestToken>> ListedAsync(Guid project) =>
        await Listing().ExecuteAsync(project, TestContext.Current.CancellationToken)
        ?? throw new InvalidOperationException("The project is there.");

    private IssueIngestToken Issuing() => new(_projects, _tokens, _cipher, _clock);

    private ListIngestTokens Listing() => new(_projects, _tokens);

    private ReadTokenBack ReadingBack() => new(_tokens, _cipher);

    private RevokeToken Revoking() => new(_tokens);
}
