using System.Text;
using Logaffe.Application.Operations;
using Logaffe.Application.Ports;
using Logaffe.Domain.Projects;
using Logaffe.Domain.Tokens;
using Logaffe.Infrastructure.Persistence;
using Logaffe.Infrastructure.Secrets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Logaffe.IntegrationTests;

/// <summary>
/// The operator's token acts over the two stores they actually run on: the rows
/// in Postgres and the key on the host volume. What a stub cannot vouch for is
/// here — that issuing writes a row the real cipher opens again, that revoking
/// is a <c>DELETE</c> another request no longer finds, and that the token which
/// came out of issuing admits a delivery until the moment it is revoked.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class TokenActsTests(PostgresFixture postgres) : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 8, 7, 9, 0, 0, TimeSpan.Zero);

    private readonly string _volume = Directory.CreateTempSubdirectory("logaffe-key-").FullName;

    public void Dispose() => Directory.Delete(_volume, recursive: true);

    [Fact]
    public async Task An_issued_token_is_read_back_out_of_the_row_and_the_key_together()
    {
        var cipher = CipherOn(_volume);
        var installation = await InstallationAsync();
        var project = await ProjectAsync(installation, "api");

        var issued = await IssueAsync(installation, cipher, project, Now);

        Assert.NotNull(issued);

        // A separate request, as the operator coming back for it would be.
        await using var context = ContextFor(installation);
        var readBack = await new ReadTokenBack(new Tokens(context), cipher)
            .IngestTokenAsync(issued.Id, TestContext.Current.CancellationToken);

        Assert.Equal(issued.Token.Text, readBack?.Text);

        // And what the column holds is not the token (ADR 0022).
        var stored = await context.IngestTokens.SingleAsync(TestContext.Current.CancellationToken);
        Assert.NotEqual(Encoding.UTF8.GetBytes(issued.Token.Secret), stored.EncryptedSecret);
    }

    [Fact]
    public async Task An_issued_token_admits_a_delivery_until_it_is_revoked()
    {
        // Revocation takes effect immediately, and this is what that means:
        // there is no cache between the two, so the next delivery is refused by
        // the same lookup that would have admitted it.
        var cipher = CipherOn(_volume);
        var installation = await InstallationAsync();
        var project = await ProjectAsync(installation, "api");

        var issued = await IssueAsync(installation, cipher, project, Now);

        Assert.Equal(project, await DeliverAsync(installation, cipher, issued!.Token));

        await using (var context = ContextFor(installation))
        {
            Assert.True(await new RevokeToken(new Tokens(context))
                .IngestTokenAsync(issued.Id, TestContext.Current.CancellationToken));
        }

        Assert.Null(await DeliverAsync(installation, cipher, issued.Token));
    }

    [Fact]
    public async Task Revoking_removes_the_row_and_leaves_the_project_where_it_was()
    {
        var cipher = CipherOn(_volume);
        var installation = await InstallationAsync();
        var project = await ProjectAsync(installation, "api");

        var issued = await IssueAsync(installation, cipher, project, Now);

        await using (var context = ContextFor(installation))
        {
            await new RevokeToken(new Tokens(context))
                .IngestTokenAsync(issued!.Id, TestContext.Current.CancellationToken);
        }

        // Removed rather than marked, and the project is not a casualty of its
        // token being retired.
        await using var reader = ContextFor(installation);
        Assert.Empty(await reader.IngestTokens.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Single(await reader.Projects.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_project_holds_two_tokens_at_most_and_the_second_one_is_the_rotation()
    {
        var cipher = CipherOn(_volume);
        var installation = await InstallationAsync();
        var project = await ProjectAsync(installation, "api");
        var other = await ProjectAsync(installation, "web");

        var first = await IssueAsync(installation, cipher, project, Now);
        var second = await IssueAsync(installation, cipher, project, Now.AddDays(30));
        Assert.NotNull(second);

        Assert.Null(await IssueAsync(installation, cipher, project, Now.AddDays(31)));

        // The count is the project's own, so another project is unaffected by
        // one that is mid-rotation.
        Assert.NotNull(await IssueAsync(installation, cipher, other, Now.AddDays(31)));

        await using var reader = ContextFor(installation);
        var listed = await new ListIngestTokens(new Projects(reader), new Tokens(reader))
            .ExecuteAsync(project, TestContext.Current.CancellationToken);

        Assert.Equal([first!.Id, second.Id], listed!.Select(token => token.Id));
        Assert.Equal(
            3, await reader.IngestTokens.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task An_agent_token_is_issued_named_renamed_listed_and_revoked()
    {
        var cipher = CipherOn(_volume);
        var installation = await InstallationAsync();

        IssuedToken issued;
        await using (var context = ContextFor(installation))
        {
            issued = await new IssueAgentToken(new Tokens(context), cipher, At(Now)).ExecuteAsync(
                "claude-code",
                AgentTokenKind.Reading,
                mayDestroy: false,
                TestContext.Current.CancellationToken);
        }

        await using (var context = ContextFor(installation))
        {
            Assert.True(await new RenameAgentToken(new Tokens(context))
                .ExecuteAsync(issued.Id, "laptop", TestContext.Current.CancellationToken));
        }

        await using (var context = ContextFor(installation))
        {
            var listed = Assert.Single(await new ListAgentTokens(new Tokens(context))
                .ExecuteAsync(TestContext.Current.CancellationToken));

            Assert.Equal("laptop", listed.Name);
            Assert.Equal(Now, listed.IssuedAt);
            Assert.Null(listed.LastUsedAt);

            // Renaming is a label and nothing the agent holds.
            var readBack = await new ReadTokenBack(new Tokens(context), cipher)
                .AgentTokenAsync(issued.Id, TestContext.Current.CancellationToken);
            Assert.Equal(issued.Token.Text, readBack?.Text);
        }

        await using (var context = ContextFor(installation))
        {
            Assert.True(await new RevokeToken(new Tokens(context))
                .AgentTokenAsync(issued.Id, TestContext.Current.CancellationToken));
        }

        await using var reader = ContextFor(installation);
        Assert.Empty(await reader.AgentTokens.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Deleting_a_project_takes_the_tokens_that_were_issued_to_it()
    {
        var cipher = CipherOn(_volume);
        var installation = await InstallationAsync();
        var project = await ProjectAsync(installation, "api");

        var issued = await IssueAsync(installation, cipher, project, Now);

        await using (var context = ContextFor(installation))
        {
            Assert.True(await new DeleteProject(new Projects(context))
                .ExecuteAsync(project, TestContext.Current.CancellationToken));
        }

        // The project, its tokens and its visibility go at once (ADR 0019), and
        // nothing in the issuing path has to remember that: the cascade on the
        // foreign key is what removes them.
        await using var reader = ContextFor(installation);
        Assert.Empty(await reader.Projects.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await reader.IngestTokens.ToListAsync(TestContext.Current.CancellationToken));

        // Which is what a sender holding one is answered by: 401 from its next
        // delivery, the same as a rotation done carelessly.
        Assert.Null(await DeliverAsync(installation, cipher, issued!.Token));
    }

    /// <summary>A migrated database with nothing in it.</summary>
    private async Task<string> InstallationAsync()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var context = ContextFor(connectionString);
        await new SchemaMigrator(context, NullLogger<SchemaMigrator>.Instance)
            .ApplyAsync(TestContext.Current.CancellationToken);

        return connectionString;
    }

    private async Task<Guid> ProjectAsync(string connectionString, string name)
    {
        await using var context = ContextFor(connectionString);
        var project = Project.Create(name, RetentionWindow.OfDays(7), Now);
        context.Projects.Add(project);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        return project.Id;
    }

    /// <summary>
    /// One issuing, with the context of its own that a request would have.
    /// </summary>
    private async Task<IssuedToken?> IssueAsync(
        string connectionString, ISecretCipher cipher, Guid project, DateTimeOffset now)
    {
        await using var context = ContextFor(connectionString);
        var issue = new IssueIngestToken(
            new Projects(context), new Tokens(context), cipher, At(now));

        return (await issue.ExecuteAsync(project, TestContext.Current.CancellationToken)).Token;
    }

    private async Task<Guid?> DeliverAsync(
        string connectionString, ISecretCipher cipher, TokenText presented)
    {
        await using var context = ContextFor(connectionString);
        var authenticate = new AuthenticateToken(
            new Tokens(context), cipher, new DummySecret(cipher), At(Now));

        return await authenticate.AdmittedProjectAsync(
            $"Bearer {presented.Text}", TestContext.Current.CancellationToken);
    }

    private static TimeProvider At(DateTimeOffset now) => new FixedClock(now);

    private static LogaffeDbContext ContextFor(string connectionString) =>
        new(new DbContextOptionsBuilder<LogaffeDbContext>().UseNpgsql(connectionString).Options);

    private static AesGcmSecretCipher CipherOn(string volumePath) =>
        new(new HostVolumeKey(volumePath, NullLogger<HostVolumeKey>.Instance));

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
