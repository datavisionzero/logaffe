using Logaffe.Domain.Projects;
using Logaffe.Domain.Tokens;
using Logaffe.Infrastructure.Persistence;
using Logaffe.Infrastructure.Secrets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Logaffe.IntegrationTests;

/// <summary>
/// The promise of ADR 0022, end to end: a token is stored encrypted and the
/// operator can read it back at any time rather than rotating and redeploying.
/// The two halves that make that true — the row in the database and the key on
/// the host volume — are both real here, because a substitute for either is a
/// substitute for the thing being proved.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class TokenReadBackTests(PostgresFixture postgres) : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 8, 6, 12, 0, 0, TimeSpan.Zero);

    private readonly string volume = Directory.CreateTempSubdirectory("logaffe-key-").FullName;

    public void Dispose() => Directory.Delete(volume, recursive: true);

    [Fact]
    public async Task An_issued_token_is_the_token_the_operator_reads_back()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var context = ContextFor(connectionString);
        await MigratorFor(context).ApplyAsync(TestContext.Current.CancellationToken);
        var cipher = CipherOn(volume);

        // Issuing: mint, seal the secret, keep the identifier in the clear.
        var issued = TokenText.Mint(TokenKind.Ingest);
        var project = Project.Create("api", RetentionWindow.OfDays(7), Now);
        context.Projects.Add(project);
        context.IngestTokens.Add(IngestToken.Issue(
            project.Id, issued.Identifier, cipher.Encrypt(issued.Secret), Now));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Reading it back: the identifier from the row, the secret out of the
        // cipher, and the two put together again.
        await using var reader = ContextFor(connectionString);
        var stored = await reader.IngestTokens.SingleAsync(TestContext.Current.CancellationToken);
        var readBack = TokenText.From(
            TokenKind.Ingest, stored.Identifier, cipher.Decrypt(stored.EncryptedSecret));

        Assert.Equal(issued.Text, readBack.Text);
    }

    [Fact]
    public async Task A_presented_token_is_found_by_its_identifier_and_matched_on_its_secret()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var context = ContextFor(connectionString);
        await MigratorFor(context).ApplyAsync(TestContext.Current.CancellationToken);
        var cipher = CipherOn(volume);

        var issued = TokenText.Mint(TokenKind.Ingest);
        var project = Project.Create("api", RetentionWindow.OfDays(7), Now);
        context.Projects.Add(project);
        context.IngestTokens.Add(IngestToken.Issue(
            project.Id, issued.Identifier, cipher.Encrypt(issued.Secret), Now));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // What a delivery does: parse the header, look the row up by the
        // identifier, decrypt once, compare in constant time (ADR 0031).
        Assert.True(TokenText.TryParse(issued.Text, out var presented));
        await using var reader = ContextFor(connectionString);
        var row = await reader.IngestTokens.SingleAsync(
            t => t.Identifier == presented.Identifier, TestContext.Current.CancellationToken);

        Assert.True(presented.SecretMatches(cipher.Decrypt(row.EncryptedSecret)));

        // And a token of the right shape that was never issued finds no row at
        // all, which is the same 401 as a wrong secret and says as little.
        var stranger = TokenText.Mint(TokenKind.Ingest);
        Assert.Null(await reader.IngestTokens.SingleOrDefaultAsync(
            t => t.Identifier == stranger.Identifier, TestContext.Current.CancellationToken));
    }

    private LogaffeDbContext ContextFor(string connectionString) =>
        new(new DbContextOptionsBuilder<LogaffeDbContext>().UseNpgsql(connectionString).Options);

    private static SchemaMigrator MigratorFor(LogaffeDbContext context) =>
        new(context, NullLogger<SchemaMigrator>.Instance);

    private static AesGcmSecretCipher CipherOn(string volumePath) =>
        new(new HostVolumeKey(volumePath, NullLogger<HostVolumeKey>.Instance));
}
