using Logaffe.Application.Operations;
using Logaffe.Domain.Operators;
using Logaffe.Domain.Projects;
using Logaffe.Domain.Tokens;
using Logaffe.Infrastructure.Persistence;
using Logaffe.Infrastructure.Secrets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Logaffe.IntegrationTests;

/// <summary>
/// The startup check with both stores real: a database that holds tokens and a
/// key file that either belongs to it or does not. Substituting either half is
/// substituting the thing being checked.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class KeyFitsTests(PostgresFixture postgres) : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 8, 6, 12, 0, 0, TimeSpan.Zero);

    private readonly List<string> volumes = [];

    public void Dispose()
    {
        foreach (var volume in volumes)
        {
            Directory.Delete(volume, recursive: true);
        }
    }

    [Fact]
    public async Task A_fresh_installation_holds_nothing_to_be_wrong_about()
    {
        var context = await MigratedAsync();

        // The ordinary first start: a key was just written beside an empty
        // database, and that is not a fault.
        Assert.Equal(KeyFit.NothingSealed, await CheckWith(context, NewVolume()));
    }

    [Fact]
    public async Task The_key_that_sealed_the_tokens_fits()
    {
        var volume = NewVolume();
        var context = await MigratedAsync();
        await SealATokenInto(context, volume);

        Assert.Equal(KeyFit.Fits, await CheckWith(context, volume));
    }

    [Fact]
    public async Task A_database_that_arrived_without_its_key_is_caught()
    {
        var context = await MigratedAsync();
        await SealATokenInto(context, NewVolume());

        // The volume is gone and the start wrote a fresh key beside a database
        // that is still full. Nothing would fail until something needed a
        // secret, which is why this is asked at startup.
        Assert.Equal(KeyFit.DoesNotFit, await CheckWith(context, NewVolume()));
    }

    [Fact]
    public async Task An_agent_token_alone_is_enough_to_check_against()
    {
        var volume = NewVolume();
        var context = await MigratedAsync();
        var cipher = CipherOn(volume);
        context.AgentTokens.Add(AgentToken.Issue(
            "terminal agent",
            AgentTokenKind.Reading,
            mayDestroy: false,
            TokenIdentifier.Mint(),
            cipher.Encrypt(TokenText.Mint(TokenKind.Agent).Secret),
            Now));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // An installation with agents and no projects yet is a real state, and
        // the sample has to reach past the ingest tokens to see it.
        Assert.Equal(KeyFit.Fits, await CheckWith(context, volume));
        Assert.Equal(KeyFit.DoesNotFit, await CheckWith(context, NewVolume()));
    }

    [Fact]
    public async Task An_operator_with_no_tokens_at_all_is_enough_to_check_against()
    {
        var volume = NewVolume();
        var context = await MigratedAsync();
        var cipher = CipherOn(volume);
        var theOperator = Operator.Claim("AQAAAAIAAYagAAAAE-not-a-real-hash", Now);
        theOperator.EnrolSecondFactor(
            cipher.Encrypt("GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ"), Now);
        context.Operators.Add(theOperator);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // An installation claimed a minute ago holds no project, no token and
        // one sealed secret — the operator's second factor. It is the case the
        // token tables miss entirely, and the one where a wrong key costs the
        // most: without it the operator cannot verify a code at all (ADR 0032).
        Assert.Equal(KeyFit.Fits, await CheckWith(context, volume));
        Assert.Equal(KeyFit.DoesNotFit, await CheckWith(context, NewVolume()));
    }

    private async Task SealATokenInto(LogaffeDbContext context, string volume)
    {
        var project = Project.Create("api", RetentionWindow.OfDays(7), Now);
        context.Projects.Add(project);
        context.IngestTokens.Add(IngestToken.Issue(
            project.Id,
            TokenIdentifier.Mint(),
            CipherOn(volume).Encrypt(TokenText.Mint(TokenKind.Ingest).Secret),
            Now));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static Task<KeyFit> CheckWith(LogaffeDbContext context, string volume) =>
        new CheckTheKeyFits(new SealedSecrets(context), CipherOn(volume))
            .ExecuteAsync(TestContext.Current.CancellationToken);

    private async Task<LogaffeDbContext> MigratedAsync()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        var context = new LogaffeDbContext(
            new DbContextOptionsBuilder<LogaffeDbContext>().UseNpgsql(connectionString).Options);

        await new SchemaMigrator(context, NullLogger<SchemaMigrator>.Instance)
            .ApplyAsync(TestContext.Current.CancellationToken);

        return context;
    }

    private string NewVolume()
    {
        var volume = Directory.CreateTempSubdirectory("logaffe-key-").FullName;
        volumes.Add(volume);
        return volume;
    }

    private static AesGcmSecretCipher CipherOn(string volume) =>
        new(new HostVolumeKey(volume, NullLogger<HostVolumeKey>.Instance));
}
