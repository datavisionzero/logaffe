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
/// Authentication over the two stores it actually runs on: the rows in Postgres
/// and the key on the host volume. What is proved here and cannot be proved
/// against a stub is that the lookup by identifier translates to SQL at all, and
/// that the coarse last-use write of ADR 0033 is a column that moves — or
/// stays — across separate requests.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class TokenAuthenticationTests(PostgresFixture postgres) : IDisposable
{
    private static readonly DateTimeOffset Issued = new(2026, 8, 7, 9, 0, 0, TimeSpan.Zero);

    private readonly string volume = Directory.CreateTempSubdirectory("logaffe-key-").FullName;

    public void Dispose() => Directory.Delete(volume, recursive: true);

    [Fact]
    public async Task An_issued_token_admits_a_delivery_to_its_project_and_a_stranger_admits_nothing()
    {
        var cipher = CipherOn(volume);
        var (connectionString, project, issued) = await IssueAsync(cipher);

        Assert.Equal(
            project,
            await DeliverAsync(connectionString, Bearer(issued), cipher, At(Issued)));

        // The same shape, never issued: one lookup that finds nothing, and an
        // answer that says no more than a wrong secret would (ADR 0031).
        var stranger = TokenText.Mint(TokenKind.Ingest);
        Assert.Null(
            await DeliverAsync(connectionString, Bearer(stranger), cipher, At(Issued)));

        // And the secret half is what admits: the right row, the wrong secret.
        var forged = TokenText.From(
            TokenKind.Ingest, issued.Identifier, TokenAlphabet.Random(TokenText.SecretLength));
        Assert.Null(
            await DeliverAsync(connectionString, Bearer(forged), cipher, At(Issued)));
    }

    [Fact]
    public async Task The_last_use_is_written_once_and_then_not_again_until_the_interval_has_passed()
    {
        var cipher = CipherOn(volume);
        var (connectionString, _, issued) = await IssueAsync(cipher);

        // Issued and never deployed, which is the case the null is for.
        Assert.Null(await StoredLastUseAsync(connectionString));

        var first = Issued.AddHours(3);
        _ = await DeliverAsync(connectionString, Bearer(issued), cipher, At(first));
        Assert.Equal(first, await StoredLastUseAsync(connectionString));

        // Every delivery in between is admitted and none of them writes: the
        // UPDATE stops scaling with traffic, which is the whole of ADR 0033.
        var within = first + AuthenticateToken.UseWriteInterval - TimeSpan.FromSeconds(1);
        _ = await DeliverAsync(connectionString, Bearer(issued), cipher, At(within));
        Assert.Equal(first, await StoredLastUseAsync(connectionString));

        var after = first + AuthenticateToken.UseWriteInterval;
        _ = await DeliverAsync(connectionString, Bearer(issued), cipher, At(after));
        Assert.Equal(after, await StoredLastUseAsync(connectionString));
    }

    /// <summary>
    /// A fresh installation holding one project and one ingest token for it.
    /// </summary>
    private async Task<(string ConnectionString, Guid Project, TokenText Issued)> IssueAsync(
        ISecretCipher cipher)
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var context = ContextFor(connectionString);
        await new SchemaMigrator(context, NullLogger<SchemaMigrator>.Instance)
            .ApplyAsync(TestContext.Current.CancellationToken);

        var issued = TokenText.Mint(TokenKind.Ingest);
        var project = Project.Create("api", RetentionWindow.OfDays(7), Issued);
        context.Projects.Add(project);
        context.IngestTokens.Add(IngestToken.Issue(
            project.Id, issued.Identifier, cipher.Encrypt(issued.Secret), Issued));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        return (connectionString, project.Id, issued);
    }

    /// <summary>
    /// One delivery, with the context of its own that a request would have.
    /// </summary>
    private async Task<Guid?> DeliverAsync(
        string connectionString, string authorization, ISecretCipher cipher, TimeProvider clock)
    {
        await using var context = ContextFor(connectionString);
        var authenticate = new AuthenticateToken(
            new Tokens(context), cipher, new DummySecret(cipher), clock);

        return await authenticate.AdmittedProjectAsync(
            authorization, TestContext.Current.CancellationToken);
    }

    private async Task<DateTimeOffset?> StoredLastUseAsync(string connectionString)
    {
        await using var context = ContextFor(connectionString);
        var token = await context.IngestTokens.SingleAsync(TestContext.Current.CancellationToken);

        return token.LastUsedAt;
    }

    private static string Bearer(TokenText token) => $"Bearer {token.Text}";

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
