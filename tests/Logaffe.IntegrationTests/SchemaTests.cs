using Logaffe.Application.Operations;
using Logaffe.Domain.Projects;
using Logaffe.Domain.Tokens;
using Logaffe.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;

namespace Logaffe.IntegrationTests;

/// <summary>
/// The schema against a real Postgres. EF Core owning the migrations is what
/// lets an upgrade be a pull and an up with no step for the operator to run, so
/// the thing worth proving is that they apply — and that applying them twice is
/// uneventful, because two containers starting at once is an ordinary event.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class SchemaTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Now = new(2026, 8, 6, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Stands in for what the cipher will produce. The schema's job is to hold
    /// the bytes and hand them back; what makes them unreadable is the key on
    /// the host volume, which is not this test's business.
    /// </summary>
    private static readonly byte[] Ciphertext = [1, 2, 3, 4];

    [Fact]
    public async Task Migrations_apply_to_an_empty_database()
    {
        var connectionString = await postgres.CreateDatabaseAsync();

        await using (var context = ContextFor(connectionString))
        {
            Assert.NotEmpty(await context.Database.GetPendingMigrationsAsync(TestContext.Current.CancellationToken));
            await MigratorFor(context).ApplyAsync(TestContext.Current.CancellationToken);
        }

        await using (var context = ContextFor(connectionString))
        {
            Assert.Empty(await context.Database.GetPendingMigrationsAsync(TestContext.Current.CancellationToken));
            Assert.NotEmpty(await context.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task An_agent_token_from_before_the_kind_existed_becomes_a_reading_one()
    {
        // The upgrade an installation that has been running since before ADR
        // 0046 makes. What `VISION.md` says is read-only by default, and this is
        // the line where the default is applied rather than asserted: a token
        // that was issued when there was only one kind is that kind afterwards,
        // and the agent holding it does not have to be reconnected.
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var context = ContextFor(connectionString);

        await context.Database.GetService<IMigrator>().MigrateAsync(
            BeforeTheKind, TestContext.Current.CancellationToken);

        var minted = TokenText.Mint(TokenKind.Agent);
        await context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO agent_token (id, name, identifier, secret, issued_at)
            VALUES ({0}, {1}, {2}, {3}, {4})
            """,
            [Guid.CreateVersion7(), "terminal agent", minted.Identifier.Value, Ciphertext, Now],
            TestContext.Current.CancellationToken);

        await MigratorFor(context).ApplyAsync(TestContext.Current.CancellationToken);

        await using var reader = ContextFor(connectionString);
        var stored = await reader.AgentTokens.SingleAsync(TestContext.Current.CancellationToken);

        Assert.Equal(AgentTokenKind.Reading, stored.Kind);
        Assert.False(stored.MayDestroy);

        // And it is still the token that was pasted into a client: the row is
        // found by the identifier the presented token carries, and nothing about
        // that changed.
        Assert.Equal(minted.Identifier, stored.Identifier);
        Assert.Equal(Ciphertext, stored.EncryptedSecret);
    }

    [Fact]
    public async Task Applying_twice_finds_nothing_to_do()
    {
        var connectionString = await postgres.CreateDatabaseAsync();

        await using var first = ContextFor(connectionString);
        await MigratorFor(first).ApplyAsync(TestContext.Current.CancellationToken);

        await using var second = ContextFor(connectionString);
        await MigratorFor(second).ApplyAsync(TestContext.Current.CancellationToken);

        Assert.Empty(await second.Database.GetPendingMigrationsAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// The upgrade promise `docs/operations.md` makes that asking for pending
    /// migrations cannot keep: on a database a later version has migrated there
    /// is nothing pending, and an old image used to go on and serve.
    /// </summary>
    [Fact]
    public async Task A_schema_from_a_newer_logaffe_is_refused()
    {
        var connectionString = await postgres.CreateDatabaseAsync();

        await using (var context = ContextFor(connectionString))
        {
            await MigratorFor(context).ApplyAsync(TestContext.Current.CancellationToken);

            // What a later version would have left behind: a row in the history
            // table naming a migration this binary was built without.
            await context.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
                VALUES ('29991231235959_SomethingThisVersionNeverHeardOf', '10.0.0')
                """,
                TestContext.Current.CancellationToken);
        }

        await using (var context = ContextFor(connectionString))
        {
            var refusal = await Assert.ThrowsAsync<SchemaIsNewerException>(
                () => MigratorFor(context).ApplyAsync(TestContext.Current.CancellationToken));

            Assert.Equal(
                ["29991231235959_SomethingThisVersionNeverHeardOf"], refusal.Migrations);
        }
    }

    [Fact]
    public async Task A_project_round_trips_with_its_retention_window()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var context = ContextFor(connectionString);
        await MigratorFor(context).ApplyAsync(TestContext.Current.CancellationToken);

        var project = Project.Create("orders-api", RetentionWindow.OfDays(14), Now);
        context.Projects.Add(project);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        await using var reader = ContextFor(connectionString);
        var stored = await reader.Projects.SingleAsync(TestContext.Current.CancellationToken);

        Assert.Equal(project.Id, stored.Id);
        Assert.Equal("orders-api", stored.Name);
        // The window is an int in the column and a window again on the way out.
        Assert.Equal(RetentionWindow.OfDays(14), stored.Retention);
        Assert.Equal(Now, stored.CreatedAt);
    }

    [Fact]
    public async Task Two_projects_cannot_share_a_name()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var context = ContextFor(connectionString);
        await MigratorFor(context).ApplyAsync(TestContext.Current.CancellationToken);

        context.Projects.Add(Project.Create("api", RetentionWindow.OfDays(7), Now));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        context.Projects.Add(Project.Create("api", RetentionWindow.OfDays(7), Now));

        // Two projects called `api` is a trap for the operator who reaches for
        // one of them at three in the morning.
        await Assert.ThrowsAsync<DbUpdateException>(
            () => context.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task An_ingest_token_round_trips_and_records_its_last_use()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var context = ContextFor(connectionString);
        await MigratorFor(context).ApplyAsync(TestContext.Current.CancellationToken);

        var project = Project.Create("api", RetentionWindow.OfDays(7), Now);
        var minted = TokenText.Mint(TokenKind.Ingest);
        var token = IngestToken.Issue(project.Id, minted.Identifier, Ciphertext, Now);
        context.Projects.Add(project);
        context.IngestTokens.Add(token);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        await using (var reader = ContextFor(connectionString))
        {
            var stored = await reader.IngestTokens.SingleAsync(TestContext.Current.CancellationToken);

            Assert.Equal(project.Id, stored.ProjectId);
            // A varchar in the column and an identifier again on the way out.
            Assert.Equal(minted.Identifier, stored.Identifier);
            Assert.Equal(Ciphertext, stored.EncryptedSecret);
            Assert.Null(stored.LastUsedAt);
        }

        token.WasUsedAt(Now.AddMinutes(1));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        await using var afterUse = ContextFor(connectionString);
        var used = await afterUse.IngestTokens.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(Now.AddMinutes(1), used.LastUsedAt);
    }

    [Fact]
    public async Task An_ingest_token_is_found_by_its_identifier()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var context = ContextFor(connectionString);
        await MigratorFor(context).ApplyAsync(TestContext.Current.CancellationToken);

        var project = Project.Create("api", RetentionWindow.OfDays(7), Now);
        var minted = TokenText.Mint(TokenKind.Ingest);
        context.Projects.Add(project);
        context.IngestTokens.Add(IngestToken.Issue(project.Id, minted.Identifier, Ciphertext, Now));
        // A second project mid-rotation, so the lookup has more than one row to
        // be wrong about.
        var other = Project.Create("web", RetentionWindow.OfDays(7), Now);
        context.Projects.Add(other);
        context.IngestTokens.Add(
            IngestToken.Issue(other.Id, TokenIdentifier.Mint(), Ciphertext, Now));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        await using var reader = ContextFor(connectionString);
        // The whole of authentication's database work: one indexed lookup on the
        // identifier the presented token carries (ADR 0031).
        var found = await reader.IngestTokens.SingleAsync(
            t => t.Identifier == minted.Identifier, TestContext.Current.CancellationToken);

        Assert.Equal(project.Id, found.ProjectId);
    }

    [Fact]
    public async Task Two_tokens_cannot_share_an_identifier()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var context = ContextFor(connectionString);
        await MigratorFor(context).ApplyAsync(TestContext.Current.CancellationToken);

        var project = Project.Create("api", RetentionWindow.OfDays(7), Now);
        var identifier = TokenIdentifier.Mint();
        context.Projects.Add(project);
        context.IngestTokens.Add(IngestToken.Issue(project.Id, identifier, Ciphertext, Now));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        context.IngestTokens.Add(IngestToken.Issue(project.Id, identifier, Ciphertext, Now));

        // Two rows answering to one identifier would make which of them was
        // meant a question the ingest path has no way to answer.
        await Assert.ThrowsAsync<DbUpdateException>(
            () => context.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Deleting_a_project_takes_its_ingest_tokens()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var context = ContextFor(connectionString);
        await MigratorFor(context).ApplyAsync(TestContext.Current.CancellationToken);

        var project = Project.Create("api", RetentionWindow.OfDays(7), Now);
        context.Projects.Add(project);
        context.IngestTokens.Add(
            IngestToken.Issue(project.Id, TokenIdentifier.Mint(), Ciphertext, Now));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        context.Projects.Remove(project);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // The project, its tokens and its visibility are gone at once
        // (ADR 0019), and the cascade is the database's rather than a step the
        // deleting code has to remember.
        await using var reader = ContextFor(connectionString);
        Assert.Empty(await reader.IngestTokens.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task An_agent_token_round_trips_and_belongs_to_no_project()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var context = ContextFor(connectionString);
        await MigratorFor(context).ApplyAsync(TestContext.Current.CancellationToken);

        var minted = TokenText.Mint(TokenKind.Agent);
        context.AgentTokens.Add(AgentToken.Issue(
            "terminal agent",
            AgentTokenKind.Reading,
            mayDestroy: false,
            minted.Identifier,
            Ciphertext,
            Now));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        await using var reader = ContextFor(connectionString);
        var stored = await reader.AgentTokens.SingleAsync(TestContext.Current.CancellationToken);

        // An agent token reads every project, so there is nothing for it to hang
        // off and no project has to exist for one to be issued.
        Assert.Equal("terminal agent", stored.Name);
        Assert.Equal(minted.Identifier, stored.Identifier);
        Assert.Null(stored.LastUsedAt);
    }

    private static LogaffeDbContext ContextFor(string connectionString) =>
        new(new DbContextOptionsBuilder<LogaffeDbContext>().UseNpgsql(connectionString).Options);

    /// <summary>
    /// The last migration before the agent token had a kind, which is the schema
    /// an installation upgrading into ADR 0046 arrives on.
    /// </summary>
    private const string BeforeTheKind = "20260819161242_HostsAndSamples";

    private static SchemaMigrator MigratorFor(LogaffeDbContext context) =>
        new(context, NullLogger<SchemaMigrator>.Instance);
}
